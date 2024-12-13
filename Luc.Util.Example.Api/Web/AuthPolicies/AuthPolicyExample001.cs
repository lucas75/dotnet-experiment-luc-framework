using Luc.Util.Web;
using Microsoft.AspNetCore.Authorization;

namespace Luc.Util.Example.Api.Web.AuthPolicies;

[LucAuthPolicy]
public static partial class AuthPolicyExample001
{
  public static void Configure( AuthorizationPolicyBuilder policy ) 
  { 
    policy.RequireAuthenticatedUser();
  }
}
