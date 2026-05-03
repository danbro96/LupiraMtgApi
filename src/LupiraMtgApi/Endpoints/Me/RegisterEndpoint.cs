using LupiraMtgApi.Handlers;
using LupiraMtgApi.Models;

namespace LupiraMtgApi.Endpoints.Me;

public static class RegisterEndpoint
{
    public static IEndpointConventionBuilder MapRegisterDevice(this IEndpointRouteBuilder app) =>
        app.MapPost("/me/register", (
                RegisterDeviceRequest? request,
                MeHandler handler,
                CancellationToken ct) => handler.RegisterAsync(request, ct))
            .AllowAnonymous()
            .WithTags("Me")
            .WithSummary("Register a new device and mint a long-lived bearer token.")
            .WithDescription(
                """
                Anonymous endpoint. Mints a fresh `sub` (Guid) and a 256-bit random bearer token
                of the form `lmtg_<base64url>`. The token is shown ONCE in the response — store it
                in secure storage on the device. The server stores only the SHA-256 hash of the
                token, so the plaintext cannot be recovered later.

                Mobile apps call this on first launch and reuse the issued token from then on.
                IP-based rate limiting prevents registration spam.
                """);
}
