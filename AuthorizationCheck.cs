/*==============================================================================
(C) Copyright 2024,2026 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Functions to parse a request context and look for authentication
              and authorization information in the x-ms-client-principal 
              header

--------------------------------------------------------------------------------
Modification History
2024-11-11 JJK  Initial version (check user role from function context for auth)
2026-08-02 JJK  Updated to use JWT token from Authorization header instead of 
                x-ms-client-principal header (as part of migrating Azure 
                Function to .NET 10 isolated worker model).  Authorization check 
                is now done by parsing the JWT token and checking for the 
                required role in the "roles" claim (on the Azure Entra ID)
================================================================================*/
using System.Text.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class AuthorizationCheck
{
    private readonly ILogger log;
    public AuthorizationCheck(ILogger logger)
    {
        log = logger;
    }

    public bool UserAuthorizedForRole(HttpRequestData req, string userRoleToCheck, out string userName)
    {
        bool userAuthorized = false;
        userName = string.Empty;

        try {
            // 1. Extract identity from bearer token (within the Authorization header of the HTTP request)
            var claimsPrincipal = ExtractPrincipal(req);
            if (claimsPrincipal == null)
            {
                log.LogWarning("No valid Authorization header found for role check. roleCheck={RoleToCheck}", userRoleToCheck);
                return false;
            }

            // 2. Check if the user has the required role
            userAuthorized = UserHasRole(claimsPrincipal, userRoleToCheck);

        } 
            catch (Exception ex) {
            log.LogWarning($"Exception in UserAuthorizedForRole, message: {ex.Message} {ex.StackTrace}");
        }

        return userAuthorized;
    }

    private ClaimsPrincipal? ExtractPrincipal(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
        {
            return null;
        }

        var bearer = authHeaders.FirstOrDefault();
        if (bearer is null || !bearer.StartsWith("Bearer "))
        {
            return null;
        }

        var token = bearer.Substring("Bearer ".Length);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return new ClaimsPrincipal(identity);
    }

    private bool UserHasRole(ClaimsPrincipal principal, string requiredRole)
    {
        return principal.Claims
            .Where(c => c.Type == "roles")
            .Any(c => string.Equals(c.Value, requiredRole, StringComparison.OrdinalIgnoreCase));
    }
}
