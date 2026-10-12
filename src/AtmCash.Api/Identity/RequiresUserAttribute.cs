namespace AtmCash.Api.Identity;

/// <summary>
/// Marks an action whose command implements IRequiresUser, so Swagger documents the required X-User header.
/// The rule itself is enforced by CurrentUserBehavior; this attribute is documentation only.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresUserAttribute : Attribute
{
}
