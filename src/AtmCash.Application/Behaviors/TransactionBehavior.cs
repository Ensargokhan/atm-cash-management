using AtmCash.Application.Abstractions.Messaging;
using AtmCash.Application.Abstractions.Persistence;
using MediatR;

namespace AtmCash.Application.Behaviors;

/// <summary>
/// Wraps every command in one database transaction and saves the unit of work at the end.
/// Row locks taken by handlers (see IAtmRepository.GetForUpdateAsync) are held until this commit,
/// and outbox messages are written in the same transaction as the change that raised them.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    public TransactionBehavior(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommandBase || _unitOfWork.HasActiveTransaction)
        {
            return await next();
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return response;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
