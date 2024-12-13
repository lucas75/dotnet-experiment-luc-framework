using System.Text.RegularExpressions;
using Luc.Util.Web;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Luc.Util.Generator;


internal partial class LucUtilTypeProcessor 
{
    private readonly LucUtilAssemblyProcessor _assemblyProcessor;
    private readonly GeneratorSyntaxContext _typeContext;
    private readonly ClassDeclarationSyntax _type;
    private readonly SemanticModel _typeSemanticModel;
    private readonly INamedTypeSymbol _typeSymbol;
    private readonly string _typeName;
    private readonly string _typeNameFull;
    private readonly string _typeNamespaceName;
    private readonly string _typeInFile;
    private readonly string _typeAssemblyName;
    
    public LucUtilTypeProcessor( LucUtilAssemblyProcessor assemblyProcessor, GeneratorSyntaxContext context ) 
    {        
        _assemblyProcessor = assemblyProcessor;
        _typeContext = context; 
        _type = (ClassDeclarationSyntax)context.Node;
        _typeSemanticModel = context.SemanticModel;
        _typeSymbol = (_typeSemanticModel.GetDeclaredSymbol(_type) as INamedTypeSymbol) ?? throw new InvalidOperationException("Type symbol not found");
        _typeName = _typeSymbol?.Name ?? throw new InvalidOperationException("Type name not found");
        _typeNameFull = _typeSymbol?.ToDisplayString() ?? throw new InvalidOperationException("Type name not found");
        _typeNamespaceName = _typeSymbol?.ContainingNamespace.ToDisplayString() ?? throw new InvalidOperationException("Namespace name not found");
        _typeInFile = _type.SyntaxTree.FilePath;    
        _typeAssemblyName = _typeContext.SemanticModel.Compilation.AssemblyName ?? throw new InvalidOperationException("Assembly name not found");                        
    }


    public void ExecutePhase1() 
    {
        DoEnforceNamingConventions();
        DoBlockOldStyleEndpoints();                
        DoGenerateEndpointMappings();
        DoGenerateAuthPolicyMappings();
        DoGenerateAuthSchemeMappings();
    }

    private void DoGenerateAuthSchemeMappings()
    {
        var attr = _typeSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == typeof(LucAuthSchemeAttribute).FullName);
      
        if
        ( 
            _typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.AuthSchemes." ) 
            && _typeSymbol.DeclaredAccessibility == Accessibility.Public 
            && attr == null 
        )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0012", 
                msgFormat: $$"""
                    Luc.Util: The type {{_typeNameFull}} must have the attribute [LucAuthScheme]
                    
                    In web Applications using the Luc.Util framework 
                        the namespace {Assembly}.Web.AuthSchemes 
                        is reserved for AuthPolicy classes. 
                    """, 
                srcLocation: _type.GetLocation() 
            );
            return;            
        }

        if (attr == null) return;


        if( _typeSymbol.LucIsPartialType() == false )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0011", 
                msgFormat: $"""Luc.Util: The type {_typeNameFull} must be a partial class""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        }


        if( !_typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.AuthSchemes." ) )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC006", 
                msgFormat: $"""Luc.Util: The type {_typeNameFull} must be in the namespace {_typeAssemblyName}.Web.AuthSchemes""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        } 

        var attrName = attr.LucGetAttributeValue( "Name" );
        var generatedMethodName = attr.LucGetAttributeValue( "GeneratedMethodName" ).LucIfNullOrEmptyReturn($"MapAuthSchemes_{_typeAssemblyName.Replace(".","")}");

        // verifica se generatedMethodName é um nome de método válido
        if( !RegexValidMethodName().IsMatch(generatedMethodName))
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0018", 
                msgFormat: $"""
                    Luc.Util: The generatedMethodName '{generatedMethodName}' is not a valid method name
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("GeneratedMethodName") 
            );
            return;
        }

         // verifica se generatedMethodName é um nome de método válido
        if( !RegexValidPropertyName().IsMatch(attrName))
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0018", 
                msgFormat: $"""
                    Luc.Util: The Name '{attrName}' needs to be a valid property name
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("Name") 
            );
            return;
        }

        _assemblyProcessor.AddGeneratedSrc_AuthSchemeMappingMethod(generatedMethodName,"");                    

        var expectedFullTypeName = $"{_typeAssemblyName}.Web.AuthSchemes.AuthScheme{attrName}";      
        if( _typeNameFull != expectedFullTypeName ) 
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0013", 
                msgFormat: $"""
                    Luc.Util: The Auth Scheme {attrName} must be implemented in the type {expectedFullTypeName}                    
                """,
                srcLocation: _type.GetLocation() 
            );
            return;
        }         



        var srcAuthPolicyMapping = $$"""

                        // The code bellow is generated based on:
                        //   AUTH SCHEME {{attrName}}
                        //   File: {{_typeInFile}} 
                        //   Line: {{_type.GetLocation().GetLineSpan().StartLinePosition.Line}}
                        //    Col: {{_type.GetLocation().GetLineSpan().StartLinePosition.Character}}
                        //   Type: {{_typeNameFull}}

                        {{_typeNameFull}}.Configure
                        (
                            services.AddAuthentication
                            (
                               "{{attrName}}"
                            )
                        );
                        ;            
 
            """;      
       
        
        _assemblyProcessor.AddGeneratedSrc_AuthPolicyMappingMethod(generatedMethodName,srcAuthPolicyMapping);                    
                
        ReportWarning
        ( 
            msgSeverity: DiagnosticSeverity.Info, 
            msgId: "LUC008", 
            msgFormat: $$"""
                Luc.Util: Fragment includedd in the generated method                                

                Fragment:

                {{srcAuthPolicyMapping}}
                """, 
            srcLocation: _type.GetLocation() 
        );          
    }



    private void DoGenerateAuthPolicyMappings()
    {
        var attr = _typeSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == typeof(LucAuthPolicyAttribute).FullName);
        
        if
        ( 
            _typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.AuthPolicies." ) 
            && _typeSymbol.DeclaredAccessibility == Accessibility.Public 
            && attr == null 
        )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0012", 
                msgFormat: $$"""
                    Luc.Util: The type {{_typeNameFull}} must have the attribute [LucAuthPolicy]
                    
                    In web Applications using the Luc.Util framework 
                        the namespace {Assembly}.Web.AuthPolicies 
                        is reserved for AuthPolicy classes. 
                    """, 
                srcLocation: _type.GetLocation() 
            );
            return;            
        }

        if (attr == null) return;

        if( _typeSymbol.LucIsPartialType() == false )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0011", 
                msgFormat: $"""Luc.Util: The type {_typeNameFull} must be a partial class""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        }

        if( !_typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.AuthPolicies." ) )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC006", 
                msgFormat: $"""Luc.Util: The type {_typeNameFull} must be in the namespace {_typeAssemblyName}.Web.AuthPolicies""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        } 

        var attrName = attr.LucGetAttributeValue( "Name" );
        var generatedMethodName = attr.LucGetAttributeValue( "GeneratedMethodName" ).LucIfNullOrEmptyReturn($"MapAuthPolicies_{_typeAssemblyName.Replace(".","")}");

        // verifica se generatedMethodName é um nome de método válido
        if( !RegexValidMethodName().IsMatch(generatedMethodName))
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0018", 
                msgFormat: $"""
                    Luc.Util: The generatedMethodName '{generatedMethodName}' is not a valid method name
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("GeneratedMethodName") 
            );
            return;
        }

         // verifica se generatedMethodName é um nome de método válido
        if( !RegexValidPropertyName().IsMatch(attrName))
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0018", 
                msgFormat: $"""
                    Luc.Util: The Name '{attrName}' needs to be a valid property name
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("Name") 
            );
            return;
        }

        _assemblyProcessor.AddGeneratedSrc_AuthPolicyMappingMethod(generatedMethodName,"");                    

        var expectedFullTypeName = $"{_typeAssemblyName}.Web.AuthPolicies.AuthPolicy{attrName}";      
        if( _typeNameFull != expectedFullTypeName ) 
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0013", 
                msgFormat: $"""
                    Luc.Util: The Auth Policy {attrName} must be implemented in the type {expectedFullTypeName}                    
                """,
                srcLocation: _type.GetLocation() 
            );
            return;
        }         

        var srcAuthPolicyMapping = $$"""

                        // The code bellow is generated based on:
                        //   POLICY {{attrName}}
                        //   File: {{_typeInFile}} 
                        //   Line: {{_type.GetLocation().GetLineSpan().StartLinePosition.Line}}
                        //    Col: {{_type.GetLocation().GetLineSpan().StartLinePosition.Character}}
                        //   Type: {{_typeNameFull}}
                                
                        options.AddPolicy(
                            "{{attrName}}", 
                            {{_typeNameFull}}.Configure
                        )
                        ;            
 
            """;      
       
        
        _assemblyProcessor.AddGeneratedSrc_AuthPolicyMappingMethod(generatedMethodName,srcAuthPolicyMapping);                    
                
        ReportWarning
        ( 
            msgSeverity: DiagnosticSeverity.Info, 
            msgId: "LUC008", 
            msgFormat: $$"""
                Luc.Util: Fragment includedd in the generated method                                

                Fragment:

                {{srcAuthPolicyMapping}}
                """, 
            srcLocation: _type.GetLocation() 
        );          
    }



    private void DoGenerateEndpointMappings()
    {
        var attr = _typeSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToString() == typeof(LucEndpointAttribute).FullName);
        
        if
        ( 
            _typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.Endpoints." ) 
            && _typeSymbol.DeclaredAccessibility == Accessibility.Public 
            && attr == null 
        ) 
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC006", 
                msgFormat: $$"""
                    Luc.Util: The type {{_typeNameFull}} must have an attribute [LucEndpoint]

                    In web Applications using the Luc.Util framework 
                          the namespace {Assembly}.Web.Endpoints
                          is reserved for LucEndpoint classes. 
                    """, 
                srcLocation: _type.GetLocation() 
            );
            return;
        }

        if (attr == null) return;

        if( !_typeNameFull.StartsWith( $"{_typeAssemblyName}.Web.Endpoints." ) )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC006", 
                msgFormat: $"""Luc.Util: The type {_typeNameFull} must be in the namespace {_typeAssemblyName}.Web.Endpoints""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        } 

        // get attribute params
        var attrMethodAndPath = attr.LucGetAttributeValue( "Path" );
        var justificationForAttributeInPath = attr.LucGetAttributeValue( "LowMaintanability_ParameterInPath_Justification" );
        var justificationForPathNotInApiManagerPrefix = attr.LucGetAttributeValue( "LowMaintanability_NotInApiManagerPath_Justification" );        

        var generatedMethodName = attr.LucGetAttributeValue("GeneratedMethodName").LucIfNullOrEmptyReturn($"MapEndpoints_{_typeAssemblyName.Replace(".","")}");
                
        // verifica se generatedMethodName é um nome de método válido
        if( !RegexValidMethodName().IsMatch(generatedMethodName))
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0018", 
                msgFormat: $"""
                    Luc.Util: The generatedMethodName '{generatedMethodName}' is not a valid method name
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("GeneratedMethodName") 
            );
            return;
        }

        _assemblyProcessor.AddGeneratedSrc_EndpointMappingMethod(generatedMethodName,"");                    

        var attrPathMatcher = EndpointPathPattern().Match(attrMethodAndPath);
        if (!attrPathMatcher.Success)
        {
            ReportWarning
            (
                msgId: "LUC0014",
                msgSeverity: DiagnosticSeverity.Error,
                msgFormat: """
                    LucUtil: The path should be 'POST /path'
                    
                    where the path should be:                    
                    * GET for operations without request body; 
                    * POST for operations with request body;
                    
                    You may other methods:
                    * PUT (not recomended);
                    * PATCH (not recomended);
                    * DELETE (not recomended);
                    * HEAD;                    
                    
                    The method is to select the input formats and the operation should be reflected on the path. 

                    The RESTful dissertation recomended an object oriented style where the http path would be the instance id and the http method the method, but this is impossible this days. The reason this doesn't work is because routing in based on the path prefix and most proxies, languages and frameworks have a very limited selection of suported methods. 
                    """,
                srcLocation: attr.LucGetAttributeArgumentLocation("Path")
            );
            return;
        }

        var attrMethod = attrPathMatcher.Groups[1].Value;
        var attrPath = attrPathMatcher.Groups[2].Value;
        if( attrPath.Contains( '{' ) && string.IsNullOrWhiteSpace( justificationForAttributeInPath ) )
        {            
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0022", 
                msgFormat: $$"""
                    Luc.Util: The utilization of parameters in path is not recomended

                    In place of: POST /myapp/mycollection/{collectionId}
                    Use          POST /myapp/mycollection?collectionId={collectionId}

                    Parameters on the path are not nammed. 
                    This is bad for mantainanbility and auditability of the applications. 
                                        
                    Browsers, CDNs, edge proxies, reverse proxies, API accelerators uses the complete URI as cache key.
                    For caching makes no difference if the parameter is in the path or in the query string. 

                    My recommendation is to put a proccess_id, user_id on the query_string even when using POST methods.
                    Even considering that POST/PUT/DELETE methods are not cached by default this is usefull to track the logs.

                    If you really need this, you can supress this warning using:
                    
                    [LucEndpoint(
                        Path = "{attrMethod} {attrPath}",
                        LowMaintanability_ParameterInPath_Justification = "I need this because of ..."
                        ...
                    )]
                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("Path")
            );    
        }
        
        var apiManagerPath = _assemblyProcessor.AppSettings?.LucUtil?.ApiManagerPath;
        if( apiManagerPath != null )
        {
            if( apiManagerPath.EndsWith( '/' ) )      
            {
                ReportWarning
                ( 
                    msgSeverity: DiagnosticSeverity.Error, 
                    msgId: "LUC06546", 
                    msgFormat: $$"""
                        Luc.Util: The appsettings.json LucUtil:ApiManagerPath must not end with a slash
                    
                        The appsettings.json of your project must contain a section like this:

                        {
                            ...
                            "LucUtil": 
                            {
                                "ApiManagerPath": "/api/v1" <-- can't end in a slash
                                "SwaggerDescription": "",
                                "SwaggerContactEmail": "",
                                "SwaggerContactPhone": "",
                                "SwaggerAuthor": ""
                            }
                        }                        

                        """, 
                    srcLocation: attr.LucGetAttributeArgumentLocation("Path") 
                );
                return;
            }
            else if( attrPath.StartsWith( $"{apiManagerPath}/" ) ) 
            {                
                if( justificationForPathNotInApiManagerPrefix.LucIsNullOrEmpty() == false ) 
                {
                    ReportWarning
                    ( 
                        msgSeverity: DiagnosticSeverity.Error, 
                        msgId: "LUC0215", 
                        msgFormat: $$"""
                            Luc.Util: Your API does not violate the rule NotInApiManagerPath!
                            
                            The use of LowMaintanability_NotInApiManagerPath_Justification is only allowed when the rule is violated

                            Api Manager Base: {{apiManagerPath}}
                            Api Path: {{attrPath}}
                            """, 
                        srcLocation: attr.LucGetAttributeArgumentLocation("Path") 
                    );
                    return;
                }
            }
            else 
            {
                if( justificationForPathNotInApiManagerPrefix.LucIsNullOrEmpty() )
                {
                    // essa regra foi desabilitada pelo dev
                }
                else
                {
                    ReportWarning
                    ( 
                        msgSeverity: DiagnosticSeverity.Error, 
                        msgId: "LUC0015", 
                        msgFormat: $$"""
                            Luc.Util: The path must start with {apiManagerPath}/

                            It is recomended that you use the same prefix that you will use to publish it.

                            If you need to disable this rule, you can use the justification LowMaintanability_NotInApiManagerPath_Justification

                            Api Manager Base: {{apiManagerPath}}
                            Api Path: {{attrPath}}
                            """, 
                        srcLocation: attr.LucGetAttributeArgumentLocation("Path") 
                    );
                    return;
                }
            }
        }
        else 
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0016", 
                msgFormat: $$"""
                    Luc.Util: The appsettings.json must declare the LucUtil:ApiManagerPath

                    The {{_typeAssemblyName}}.csproj must contains

                    <ItemGroup>
                        <AdditionalFiles Include="appsettings.json" />
                    </ItemGroup>    
                
                    And the appsettings.json of your project must contain a section like this:

                    {
                        ...
                        "LucUtil": 
                        {
                            "ApiManagerPath": "/api/v1"
                            "SwaggerDescription": "",
                            "SwaggerContactEmail": "",
                            "SwaggerContactPhone": "",
                            "SwaggerAuthor": ""
                        }
                    }                        

                    """, 
                srcLocation: attr.LucGetAttributeArgumentLocation("Path") 
            );
            return;
        }

        var expectedTypeNameBase = $"{_typeAssemblyName}.Web.Endpoints";      
        var expectedTypeNameReference = attrPath[apiManagerPath.Length..].Trim('/');        
        expectedTypeNameReference = expectedTypeNameReference.Replace( "{", "param-" );
        expectedTypeNameReference = expectedTypeNameReference.Replace( "}", "" );

        var expectedShortTypeName = expectedTypeNameReference.LucGetFileNameFromUrl().Trim('/').LucPathElementToCamelCase();   
        var expectedNamespace = expectedTypeNameReference.LucGetDirNameFromUrlPath().Trim('/').LucPathToCamelCase();
                
        var expectedFullTypeName = 
            expectedNamespace.LucIsNullOrEmpty() ?
                $"{expectedTypeNameBase}.Endpoint{expectedShortTypeName}"
            :
                $"{expectedTypeNameBase}.{expectedNamespace}.Endpoint{expectedShortTypeName}";        
        ;

        if( _typeNameFull != expectedFullTypeName ) 
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC0013", 
                msgFormat: $"""
                    Luc.Util: The path {attrPath} must be implemented in the type {expectedFullTypeName}

                    expectedTypeNameReference: {expectedTypeNameReference}
                    expectedShortTypeName: {expectedShortTypeName}
                    expectedNamespace: {expectedNamespace}
                    expectedFullTypeName: {expectedFullTypeName}
                """,
                srcLocation: _type.GetLocation() 
            );
            return;
        }         

        switch( attrMethod.ToUpper() )
        {
            case "GET": attrMethod = "Get"; break;
            case "POST": attrMethod = "Post"; break;
            case "PUT": attrMethod = "Put"; break;
            case "PATCH": attrMethod = "Patch"; break;
            case "DELETE": attrMethod = "Delete"; break;
            case "HEAD": attrMethod = "Head"; break;
            default: 
                ReportWarning
                ( 
                    msgSeverity: DiagnosticSeverity.Error, 
                    msgId: "LUC0017", 
                    msgFormat: $"""Luc.Util: The method {attrMethod} is not supported by dotnet core minimal APIs""", 
                    srcLocation: attr.LucGetAttributeArgumentLocation("Path") 
                );
                return;
        }


        var swaggerGroupTitle = attr.LucGetAttributeValue("SwaggerGroupTitle");
        var swaggerFuncSummary = attr.LucGetAttributeValue("SwaggerFuncSummary");
        var swaggerFuncDescription = attr.LucGetAttributeValue("SwaggerFuncDescription");
        var swaggerFuncName = attr.LucGetAttributeValue("SwaggerFuncName");
        var authPolicy = attr.LucGetAttributeValue("AuthPolicy");

        var srcFuncNameFragment = 
            swaggerFuncName.LucIsNullOrEmpty() ? 
                "" 
            : 
                $$"""
                        .WithDisplayName( {{SymbolDisplay.FormatLiteral(swaggerFuncName,true)}} )
                """;


        var srcEndpointMapping = $$"""

                    // The code bellow is generated based on:
                    //   ENDPOINT {{attrMethod.ToUpper()}} {{attrPath}}
                    //   File: {{_typeInFile}} 
                    //   Line: {{_type.GetLocation().GetLineSpan().StartLinePosition.Line}}
                    //    Col: {{_type.GetLocation().GetLineSpan().StartLinePosition.Character}}
                    //   Type: {{_typeNameFull}}
        
                    app.Map{{attrMethod}}(
                        "{{attrPath}}", 
                        {{expectedFullTypeName}}.Execute
                    )
                    {{srcFuncNameFragment}}
                    .WithTags( [ {{SymbolDisplay.FormatLiteral(swaggerGroupTitle,true)}} ] )
                    .WithSummary( {{SymbolDisplay.FormatLiteral(swaggerFuncSummary,true)}} )
                    .WithDescription( {{SymbolDisplay.FormatLiteral(swaggerFuncSummary,true)}} )
                    .RequireAuthorization( {{SymbolDisplay.FormatLiteral(authPolicy,true)}} )                          
                    ;            

            """;

       
        
        _assemblyProcessor.AddGeneratedSrc_EndpointMappingMethod(generatedMethodName,srcEndpointMapping);                    
                

        ReportWarning
        ( 
            msgSeverity: DiagnosticSeverity.Info, 
            msgId: "LUC008", 
            msgFormat: $$"""
                Luc.Util: Fragment includedd in the generated method
                                
                Mapping: {{attrMethodAndPath}}
                Method: {{expectedFullTypeName}}.Execute
                
                The generated fragment is:

                {{srcEndpointMapping}}
                """, 
            srcLocation: _type.GetLocation() 
        );        
    }
    
    private void DoEnforceNamingConventions() 
    {
        var wrongNamespace = $"{_typeAssemblyName}.src.";

        if( _typeNameFull.StartsWith( wrongNamespace, StringComparison.InvariantCultureIgnoreCase ) ) 
        {      
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC005", 
                msgFormat: """Luc.Util: The namespace can't contain the 'src' element.""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        }
        
        if( !_typeNameFull.StartsWith( $"{_typeAssemblyName}.", StringComparison.InvariantCulture ) )
        {
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC003", 
                msgFormat: $"""Luc.Util: The type name '{_typeName}' must start with '{_typeAssemblyName}'.""", 
                srcLocation: _type.GetLocation() 
            );
            return;
        }

        if( _typeSymbol.DeclaredAccessibility == Accessibility.Public )
        {      
            var relativeTypeName = _typeNameFull.Replace( _typeAssemblyName+".", "");
            var expectedFile = $"{relativeTypeName.Replace(".","/")}";
            var expectedFileDir = Path.GetDirectoryName( expectedFile ) ?? "";
            var expectedFileName = Path.GetFileName( expectedFile ) ?? "";
            expectedFile += ".cs";

            var typeInFileDir = Path.GetDirectoryName( _typeInFile ) ?? "";
            var typeInFileName = Path.GetFileNameWithoutExtension( _typeInFile ) ?? "";
                      
            if( _typeSymbol.LucIsPartialType() ) 
            {
                /*if( !typeInFileDir.EndsWith( expectedFileDir ) || typeInFileName.StartsWith( $"{expectedFileName}_" ) || typeInFileName == expectedFileName )
                {                 
                    ReportWarning
                    ( 
                        msgSeverity: DiagnosticSeverity.Error, 
                        msgId: "LUC012", 
                        msgFormat: $"""Luc.Util: The type {_typeNameFull} must be in the source file {expectedFileDir}/{expectedFileName}_*.cs""", 
                        srcLocation: _type.GetLocation() 
                    );
                    return;
                }*/
            }
            else 
            {
                if( !_typeInFile.EndsWith( expectedFile ) )
                {                 
                    ReportWarning
                    ( 
                        msgSeverity: DiagnosticSeverity.Error, 
                        msgId: "LUC004", 
                        msgFormat: $"""Luc.Util: The type {_typeNameFull} must be in the source file {expectedFile}""", 
                        srcLocation: _type.GetLocation() 
                    );
                    return;
                }           
            }                                 
        }
    }

    private void DoBlockOldStyleEndpoints() 
    {
        // verifica se o tipo é um controller, se for, gera um erro
        if (_typeSymbol.BaseType?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ControllerBase")
        {            
            ReportWarning
            ( 
                msgSeverity: DiagnosticSeverity.Error, 
                msgId: "LUC001", 
                msgFormat: $"""Luc.Util: The utilization of Controller is forbidden! Use [LucEndpoint] instead.""", 
                srcLocation: _type.GetLocation() 
            );
        }

        foreach( var attr in _typeSymbol.GetAttributes() )
        {
            if (attr.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ControllerAttribute")
            {                
                ReportWarning
                ( 
                    msgSeverity: DiagnosticSeverity.Error, 
                    msgId: "LUC002", 
                    msgFormat: $"""Luc.Util: The utilization of [Controller] is forbidden! Use [LucEndpoint] instead.""", 
                    srcLocation: attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() 
                );
            }
        }
    }

    private void ReportWarning( DiagnosticSeverity msgSeverity, string msgId, string msgFormat, Location? srcLocation, params object[] msgArgs ) 
    {
        _assemblyProcessor.ReportWarning( 
            msgSeverity: msgSeverity, 
            msgId: msgId, 
            msgFormat: msgFormat, 
            srcLocation: srcLocation, 
            msgArgs: msgArgs 
        );
    }

    [GeneratedRegex("^(\\S*) (/.*)$", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex EndpointPathPattern();

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex RegexValidMethodName();

    [GeneratedRegex("^[A-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex RegexValidPropertyName();
}
