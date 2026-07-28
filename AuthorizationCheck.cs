/*==============================================================================
(C) Copyright 2024 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Functions to parse a request context and look for authentication
              and authorization information in the x-ms-client-principal 
              header

--------------------------------------------------------------------------------
Modification History
2024-11-11 JJK  Initial version (check user role from function context for auth)
================================================================================*/
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class AuthorizationCheck
{
    private readonly ILogger log;
    public AuthorizationCheck(ILogger logger)
    {
        log = logger;
    }

    private class ClientPrincipalClaim
    {
        [JsonPropertyName("typ")]
        public string? Type { get; set; }
        [JsonPropertyName("val")]
        public string? Value { get; set; }
    }

    private class ClientPrincipal
    {
        [JsonPropertyName("userId")]
        public string? userId { get; set; }
        [JsonPropertyName("userRoles")]
        public string[]? userRoles { get; set; }
        [JsonPropertyName("identityProvider")]
        public string? identityProvider { get; set; }
        [JsonPropertyName("userDetails")]
        public string? userDetails { get; set; }
        

        [JsonPropertyName("auth_typ")]
        public string? IdentityProvider { get; set; }
        [JsonPropertyName("name_typ")]
        public string? NameClaimType { get; set; }
        [JsonPropertyName("role_typ")]
        public string? RoleClaimType { get; set; }
        

        [JsonPropertyName("claims")]
        public IEnumerable<ClientPrincipalClaim>? Claims { get; set; }
    }

    public bool UserAuthorizedForRole(HttpRequestData req, string userRoleToCheck, out string userName)
    {
        bool userAuthorized = false;
        userName = "";

        try {
            // Use a Security ClaimsPrincipal to check authentication and authorization
            ClaimsPrincipal claimsPrincipal = new ClaimsPrincipal();

            // Get the MS client principal from the request header
            if (req.Headers.TryGetValues("x-ms-client-principal", out var headerValues))
            {
                var headerValue = headerValues.FirstOrDefault() ?? "";
                var decoded = Convert.FromBase64String(headerValue);
                var jsonStr = Encoding.UTF8.GetString(decoded);
                // Deserialize the JSON to get values into a class
                var clientPrincipal = JsonSerializer.Deserialize<ClientPrincipal>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Check if the identity provider has already created the actual Claims, or if they have to be built from the Roles
                if (clientPrincipal!.Claims == null) {
                    if (!string.IsNullOrWhiteSpace(clientPrincipal.identityProvider)) {
                        if (clientPrincipal.identityProvider.Equals("aad")) {
                            // If a managed identity from Azure Active Directory (AAD), construct the claims from the user Roles
                            var claims = new List<Claim>();
                            claims.Add(new Claim(ClaimTypes.Name, clientPrincipal.userDetails!));
                            foreach (string userRole in clientPrincipal.userRoles!) {
                                claims.Add(new Claim(ClaimTypes.Role, userRole));
                            }
                            // When using Azure Active Directory (AAD) with a ClaimsPrincipal, the authentication type is typically "Bearer" for OAuth 2.0 tokens    
                            var claimsIdentity = new ClaimsIdentity(claims, "Bearer"); 
                            claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
                        }
                    }
                } else {
                    var claimsIdentity = new ClaimsIdentity(clientPrincipal.IdentityProvider, clientPrincipal.NameClaimType, clientPrincipal.RoleClaimType);
                    claimsIdentity.AddClaims(clientPrincipal.Claims.Select(c => new Claim(c.Type!, c.Value!)));
                    claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
                }

                // Use the constructed ClaimsPrincipal to check if the user is authenticated, and in the authorized Role (to return a YES or NO to the caller)
                if (claimsPrincipal.Identity!.IsAuthenticated) {
                    userName = claimsPrincipal.Identity.Name ?? "";
                    userAuthorized = claimsPrincipal.IsInRole(userRoleToCheck);
                }
            }
        } 
            catch (Exception ex) {
            log.LogWarning($"Exception in UserAuthorizedForRole, message: {ex.Message} {ex.StackTrace}");
        }

        return userAuthorized;
    }

    /*
    public ClaimsPrincipal Parse(HttpRequestData req)
    {
        ClaimsPrincipal claimsPrincipal = new ClaimsPrincipal();

        if (req.Headers.TryGetValues("x-ms-client-principal", out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            var decoded = Convert.FromBase64String(headerValue);
            var jsonStr = Encoding.UTF8.GetString(decoded);
            var clientPrincipal = JsonSerializer.Deserialize<ClientPrincipal>(jsonStr, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (clientPrincipal.Claims == null) {
                if (clientPrincipal.identityProvider.Equals("aad")) {
                    var claims = new List<Claim>();
                    claims.Add(new Claim(ClaimTypes.Name, clientPrincipal.userDetails));
                    foreach (string userRole in clientPrincipal.userRoles) {
                        claims.Add(new Claim(ClaimTypes.Role, userRole));
                    }
                    // When using Azure Active Directory (AAD) with a ClaimsPrincipal, the authentication type is typically "Bearer" for OAuth 2.0 tokens    
                    var claimsIdentity = new ClaimsIdentity(claims, "Bearer"); 
                    claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
                }
            } else {
                var claimsIdentity = new ClaimsIdentity(clientPrincipal.IdentityProvider, clientPrincipal.NameClaimType, clientPrincipal.RoleClaimType);
                claimsIdentity.AddClaims(clientPrincipal.Claims.Select(c => new Claim(c.Type, c.Value)));
                claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
            }

        }

         //*  At this point, the code can iterate through `principal.Claims` to
         //*  check claims as part of validation. Alternatively, we can convert
         //*  it into a standard object with which to perform those checks later
         //*  in the request pipeline. That object can also be leveraged for 
         //*  associating user data, etc. The rest of this function performs such
         //*  a conversion to create a `ClaimsPrincipal` as might be used in 
         //*  other .NET code.

        return claimsPrincipal;
    }
    */
}
