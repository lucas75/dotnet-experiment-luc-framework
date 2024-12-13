using Luc.Web.Example.Api.Web.AuthPolicies;
using Luc.Web.Interface;

namespace Luc.Web.Example.Api.Web.Endpoints;


public static partial class EndpointTeste 
{ 
    [LucEndpoint(
      Path = "GET /prefixo-no-api-manager/teste",
      AuthPolicy = typeof(AuthPolicyExample001),
      SwaggerFuncName = "nome da api de testes",
      SwaggerFuncSummary = "sumário da api de testes",
      SwaggerFuncDescription = "descrição da api de testes",
      SwaggerGroupTitle = "grupo da api de testes"
    )]
    public static void Execute
    ( 
      HttpContext ctx 
    ) 
    {
      // teste    
    }
}

 