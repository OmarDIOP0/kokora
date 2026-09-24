using System.Security.Cryptography;

namespace Kokora.Web.Infrastructure;

/// <summary>
/// En-têtes de sécurité de toutes les réponses. Politique CSP : scripts du site uniquement (plus les scripts en ligne
/// portant le jeton « nonce » de la requête), aucun contenu tiers, aucune intégration dans un cadre.
/// 'unsafe-eval' reste nécessaire à Alpine.js (expressions x-data, x-show…), qui ne lit que des attributs écrits par le serveur.
/// </summary>
public static class SecurityHeaders
{
    public const string NonceKey = "csp-nonce";

    public static string Nonce(this HttpContext context) => context.Items[NonceKey] as string ?? "";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool isDevelopment) => app.Use(async (context, next) =>
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // hexadécimal : aucun caractère à encoder
        context.Items[NonceKey] = nonce;
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["Content-Security-Policy"] = string.Join("; ", new[]
            {
                "default-src 'self'",
                $"script-src 'self' 'nonce-{nonce}' 'unsafe-eval'",
                "style-src 'self' 'unsafe-inline'",   // attributs style (couleurs des équipes), styles injectés par les bibliothèques
                "img-src 'self' data: blob: https:",
                "font-src 'self' data:",
                "connect-src 'self' ws: wss:",        // SignalR (WebSockets)
                "worker-src 'self'",
                "manifest-src 'self'",
                "frame-ancestors 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "object-src 'none'",
                isDevelopment ? "" : "upgrade-insecure-requests"
            }.Where(d => d.Length > 0));
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        await next();
    });
}
