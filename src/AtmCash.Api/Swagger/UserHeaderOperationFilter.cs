using AtmCash.Api.Identity;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AtmCash.Api.Swagger;

/// <summary>Documents the required X-User header for actions marked with <see cref="RequiresUserAttribute"/>.</summary>
public sealed class UserHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresUser = context.MethodInfo.GetCustomAttributes(typeof(RequiresUserAttribute), inherit: true).Any();
        if (!requiresUser)
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = HeaderCurrentUser.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "Staff user id: ASCII letters, digits, '.', '_' or '-' (e.g. ensar.gokhan).",
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
