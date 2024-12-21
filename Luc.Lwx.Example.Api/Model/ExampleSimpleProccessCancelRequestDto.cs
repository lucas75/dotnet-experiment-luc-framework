using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Luc.Lwx.Example.Api.Model;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class ExampleSimpleProccessCancelRequestDto
{
    [JsonPropertyName("ok")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Ok { get; set; }
}