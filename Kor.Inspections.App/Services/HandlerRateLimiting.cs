using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace Kor.Inspections.App.Services
{
    /// <summary>
    /// Named rate-limit policies enforced per Razor Page handler method.
    /// The built-in rate-limiting middleware only honors [EnableRateLimiting]
    /// at the endpoint level, and Razor Pages compile to a single endpoint per
    /// page — so handler-level attributes were silently ignored (verified
    /// empirically 2026-07-22: 14 rapid requests to a 10/10min handler all
    /// returned 200). <see cref="PageHandlerRateLimitFilter"/> reads the
    /// attribute from the selected handler method and enforces the matching
    /// policy here instead.
    /// </summary>
    public sealed class HandlerRateLimiterService : IDisposable
    {
        private readonly Dictionary<string, PartitionedRateLimiter<HttpContext>> _limiters;

        public HandlerRateLimiterService()
        {
            _limiters = new Dictionary<string, PartitionedRateLimiter<HttpContext>>(StringComparer.Ordinal)
            {
                ["booking"] = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetUserOrIpPartitionKey(httpContext),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(30),
                            QueueLimit = 0
                        })),

                ["verification"] = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetUserOrIpPartitionKey(httpContext),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(10),
                            QueueLimit = 0
                        })),

                ["contactMutation"] = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(10),
                            QueueLimit = 0
                        }))
            };
        }

        public ValueTask<RateLimitLease> AcquireAsync(
            string policyName,
            HttpContext httpContext,
            CancellationToken ct = default)
        {
            if (!_limiters.TryGetValue(policyName, out var limiter))
                throw new InvalidOperationException($"Unknown rate limit policy '{policyName}'.");

            return limiter.AcquireAsync(httpContext, permitCount: 1, ct);
        }

        internal static string GetUserOrIpPartitionKey(HttpContext httpContext)
        {
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? httpContext.User.Identity?.Name;

                if (!string.IsNullOrWhiteSpace(userId))
                    return $"user:{userId}";
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString();
            return string.IsNullOrWhiteSpace(ip) ? "ip:unknown" : $"ip:{ip}";
        }

        public void Dispose()
        {
            foreach (var limiter in _limiters.Values)
                limiter.Dispose();
        }
    }

    /// <summary>
    /// Global page filter that enforces [EnableRateLimiting("...")] attributes
    /// placed on Razor Page handler methods. Handlers without the attribute are
    /// unaffected. Rejections return the same 429 JSON payload the middleware's
    /// OnRejected callback previously produced, so client-side handling
    /// (describeHttpError) keeps working unchanged.
    /// </summary>
    public sealed class PageHandlerRateLimitFilter : IAsyncPageFilter
    {
        private static readonly ConcurrentDictionary<MethodInfo, string?> PolicyCache = new();

        private readonly HandlerRateLimiterService _limiters;

        public PageHandlerRateLimitFilter(HandlerRateLimiterService limiters)
        {
            _limiters = limiters;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context,
            PageHandlerExecutionDelegate next)
        {
            var methodInfo = context.HandlerMethod?.MethodInfo;
            var policyName = methodInfo == null
                ? null
                : PolicyCache.GetOrAdd(
                    methodInfo,
                    static mi => mi.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);

            if (policyName != null)
            {
                using var lease = await _limiters.AcquireAsync(
                    policyName,
                    context.HttpContext,
                    context.HttpContext.RequestAborted);

                if (!lease.IsAcquired)
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Result = new JsonResult(new
                    {
                        error = "Too many requests. Please wait a moment and try again."
                    });
                    return;
                }
            }

            await next();
        }
    }
}
