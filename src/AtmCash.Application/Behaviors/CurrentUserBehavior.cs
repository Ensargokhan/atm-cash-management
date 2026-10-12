using System.Text.RegularExpressions;
using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Services;
using AtmCash.Application.Common;
using AtmCash.Domain.Common;
using MediatR;

namespace AtmCash.Application.Behaviors;

/// <summary>
/// For <see cref="IRequiresUser"/> commands the caller must identify themselves with a valid user id
/// (ASCII letters, digits, '.', '_' or '-'; at most 100 characters, e.g. "ensar.gokhan").
/// Runs before validation and the transaction, so an anonymous or malformed request fails fast.
/// </summary>
public sealed class CurrentUserBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly Regex UserIdFormat = new(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

    private readonly ICurrentUser _currentUser;

    public CurrentUserBehavior(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IRequiresUser)
        {
            return await next();
        }

        var userId = _currentUser.UserId;

        if (userId is null)
        {
            throw new BadRequestException(ErrorCodes.UserHeaderRequired,
                "This operation is performed by staff: identify yourself with the X-User header.");
        }

        if (!UserIdFormat.IsMatch(userId))
        {
            throw new BadRequestException(ErrorCodes.InvalidUser,
                "The user id may contain only ASCII letters, digits, '.', '_' and '-' (at most 100 characters), e.g. 'ensar.gokhan'.");
        }

        return await next();
    }
}
