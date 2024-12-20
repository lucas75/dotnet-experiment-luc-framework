using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Luc.Lwx.Generator;

[SuppressMessage("","S101")]
internal partial class LwxGenerator_Assembly
{  
    public SourceProductionContext Context { get; private set; }
    public ImmutableArray<GeneratorSyntaxContext> TypeSymbols { get; private set; }
    public string ProjectDir { get; private set; }
    public AppSettingsDto? AppSettings { get; private set; }
    private readonly string? _assemblyName;

    public Dictionary<string,List<LwxGenerator_Method_Endpoint>> EndpointMappingMethods { get; internal set; } = [];
    public Dictionary<string,List<LwxGenerator_Method_AuthPolicy>> PolicyTypes { get; internal set; } = [];
    public Dictionary<string,List<LwxGenerator_Method_AuthScheme>> SchemeTypes { get; internal set; } = [];

    public void AddEndpointMappingMethod(string group, LwxGenerator_Method_Endpoint methodSrc)
    {
        var groupMap = EndpointMappingMethods.GetValueOrDefault(group);
        if( groupMap == null )
        {
            groupMap = [];
            EndpointMappingMethods.Add(group, groupMap);
        }
        groupMap.Add(methodSrc);        
    }

    // FUTURE: improve this to pass only a descriptor with the essential fields 
    public void AddPolicyType(string group, LwxGenerator_Method_AuthPolicy processor )
    {
        var groupMap = PolicyTypes.GetValueOrDefault(group);
        if( groupMap == null )
        {
            groupMap = [];
            PolicyTypes.Add(group, groupMap);
        }
        groupMap.Add(processor);        
    }

    // FUTURE: improve this to pass only a descriptor with the essential fields
    public void AddSchemeType(string group, LwxGenerator_Method_AuthScheme processor )
    {
        var groupMap = SchemeTypes.GetValueOrDefault(group);
        if( groupMap == null )
        {
            groupMap = [];
            SchemeTypes.Add(group, groupMap);
        }
        groupMap.Add(processor);        
    }


    public LwxGenerator_Assembly
    (
        SourceProductionContext sourceProductionContext, 
        ImmutableArray<string?> appSettingsFiles, 
        ImmutableArray<GeneratorSyntaxContext> typeSymbols
    ) 
    { 
        Context = sourceProductionContext;
        TypeSymbols = typeSymbols;

        AppSettingsDto? appSettings = null;            
        try 
        {
            var appSettingsText = appSettingsFiles.FirstOrDefault();
            if( appSettingsText != null )
            {
                appSettings = JsonSerializer.Deserialize<AppSettingsDto>(appSettingsText);
            }
        }
        catch( Exception ex )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0911", 
                msgFormat: $"""
                    Can't parse the appsettings.json file:

                    {ex.Message}
                    """, 
                srcLocation: null 
            );
        }     
        appSettings ??= new AppSettingsDto();   
        appSettings.Lwx ??= new AppSettingsSectionDto();   
        AppSettings = appSettings;
        
        
        if(typeSymbols.FirstOrDefault().Node is TypeDeclarationSyntax firstType)
        {
            ProjectDir = Path.GetDirectoryName(firstType.SyntaxTree.FilePath) ?? throw new InvalidOperationException("Project directory not found");
        }
        else
        {
            throw new InvalidOperationException("Project directory not found");
        }               

        if( typeSymbols.Length != 0 )
        {
            _assemblyName = typeSymbols[0].SemanticModel.Compilation.AssemblyName ?? throw new InvalidOperationException("Assembly name not found");                        
        }                
    }    

    public void Execute() 
    {
        foreach ( var typeSymbol in TypeSymbols )
        {            
            try 
            {
                var processClass = new LwxGenerator_Type(this, typeSymbol);                
                processClass.ExecutePhase1();                       
            }
            catch( Exception ex )
            {
                ReportWarning
                ( 
                    msgSeverity: DiagnosticSeverity.Error, 
                    msgId: "LUC0912", 
                    msgFormat: $"""
                        The LucWebGenerator threw an exception: 
                        
                        {ex.Message},
                        {ex.StackTrace}
                        """, 
                    srcLocation: typeSymbol.Node.GetLocation() 
                );
            }    
        }

        /*
        // Parse the whole syntax tree to identify method calls of WebApplication.MapGet, MapPost, MapDelete, MapPut, etc.
        var minimalApiMethods = new[] 
        { 
            "MapGet", 
            "MapPost", 
            "MapDelete", 
            "MapPut", 
            "MapPatch", 
            "MapOptions", 
            "MapHead" 
        };
        foreach (var typeSymbol in TypeSymbols)
        {
            var root = typeSymbol.Node.SyntaxTree.GetRoot();
            var methodCalls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                                     minimalApiMethods.Contains(memberAccess.Name.Identifier.Text));

            foreach (var methodCall in methodCalls)
            {
                var semanticModel = typeSymbol.SemanticModel;
                var symbolInfo = semanticModel.GetSymbolInfo(methodCall);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (methodSymbol != null && methodSymbol.ContainingType.ToDisplayString() == typeof(WebApplication).FullName)
                {
                    var containingClass = methodCall.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                    if (containingClass != null)
                    {
                        var classSymbol = semanticModel.GetDeclaredSymbol(containingClass);
                        if (classSymbol != null && !classSymbol.ContainingNamespace.ToDisplayString().EndsWith(".Generated"))
                        {
                            ReportWarning
                            (
                                msgSeverity: DiagnosticSeverity.Error,
                                msgId: "LUC0913",
                                msgFormat: $"WebApplication.{methodSymbol.Name} should only be called from generated classes.",
                                srcLocation: methodCall.GetLocation()
                            );
                        }
                    }
                }
            }
        }
*/
        GenerateEndpointMappings();
        GenerateAuthPolicyMappings();
        GenerateAuthSchemeMappings();
          
    }

    private void GenerateEndpointMappings() 
    {
        var srcMethods = new StringBuilder();

        foreach( var group in EndpointMappingMethods )
        {
            var srcMethodBody = new StringBuilder();
            foreach (var methodSrc in group.Value)
            {
                srcMethodBody.Append(methodSrc.EndpointSrcMethodBody);
            }

            srcMethods.Append($$"""
                
                        public static void {{group.Key}}(this IEndpointRouteBuilder app)
                        {
                            {{srcMethodBody}}
                        }
                    
                """);
        }         
    
        var srcExtension = new StringBuilder();
        srcExtension.AppendLine($$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.Logging;                 
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.AspNetCore.Authorization;
        
            namespace {{_assemblyName}}.Generated
            {
                public static class GeneratedEndpointMappings
                {
                    {{srcMethods}}
                }
            }
            """);

        Context.AddSource("GeneratedEndpointMappings.g.cs", srcExtension.ToString()); 
    }

    private void GenerateAuthPolicyMappings() 
    {
        var srcMethods = new StringBuilder();
        var srcOthers = new StringBuilder();

        foreach( var group in PolicyTypes )
        {
            var srcMethodBody = new StringBuilder();
            foreach (var method in group.Value)
            {
                srcMethodBody.Append(method.AuthPolicySrcMethodBody);
                srcOthers.Append(method.AuthPolicySrcIdClass);
            }

            srcMethods.Append($$"""
                
                    public static void {{group.Key}}(this WebApplicationBuilder builder)
                    {
                        builder.Services.{{group.Key}}();
                    }

                    public static void {{group.Key}}(this IServiceCollection services)
                    {
                        services.AddAuthorization( options => 
                        {
                            {{srcMethodBody}}
                        });
                    }
                    
                """);
        }         
     
        var srcExtension = new StringBuilder();
        srcExtension.AppendLine($$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;                        
        
            namespace {{_assemblyName}}.Generated 
            {
                public static class GeneratedAuthPolicyMappings
                {
                    {{srcMethods}}
                }
            }

            {{srcOthers}}
            """);

        Context.AddSource("GeneratedAuthPolicyMappings.g.cs", srcExtension.ToString()); 
    }


    private void GenerateAuthSchemeMappings() 
    {        
        var srcMethods = new StringBuilder();
        var srcOthers = new StringBuilder();

        foreach( var group in SchemeTypes )
        {
            var srcMethodBody = new StringBuilder();
            foreach (var method in group.Value)
            {
                srcMethodBody.Append(method.AuthSchemeSrcMethodBody);              
                srcOthers.Append(method.AuthSchemeSrcIdClass);
            }

            srcMethods.Append($$"""
                    
                        public static void {{group.Key}}(this WebApplicationBuilder builder)
                        {
                            builder.Services.{{group.Key}}();
                        }

                        public static void {{group.Key}}(this IServiceCollection services)
                        {
                            {{srcMethodBody}}
                        }
                    
                """);
        }         
     
        var srcExtension = new StringBuilder();
        srcExtension.AppendLine($$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;   
            using Microsoft.AspNetCore.Authentication;
            using Luc.Lwx;        
            using Microsoft.Extensions.Logging;
            using Microsoft.Extensions.Options;
            using System.Text.Encodings.Web;
            using System.Threading.Tasks;             
        
            namespace {{_assemblyName}}.Generated 
            {
                public static class GeneratedAuthSchemeMappings
                {
                    {{srcMethods}}
                }
            }

            {{srcOthers}}
            """);

        Context.AddSource("GeneratedAuthSchemeMappings.g.cs", srcExtension.ToString()); 
    }


    internal void ReportWarning( DiagnosticSeverity msgSeverity, string msgId, string msgFormat, Location? srcLocation, params object[] msgArgs ) 
    {
        Context.ReportDiagnostic
        ( 
            Diagnostic.Create
            (
                new DiagnosticDescriptor
                (
                    id: msgId,
                    title: msgFormat,
                    messageFormat: msgFormat,
                    category: LwxGenerator.LwxEndpointCategory,
                    msgSeverity,
                    isEnabledByDefault: true
                ), 
                srcLocation, 
                msgArgs
            )
        );
    }
}
