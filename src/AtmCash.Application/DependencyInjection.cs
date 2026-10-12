using AtmCash.Application.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AtmCash.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters: outermost first.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(CurrentUserBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>)); // inside the transaction
        });

        services.AddAutoMapper(assembly);
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<IdempotencyContext>();

        return services;
    }
}
