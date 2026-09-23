using GenAIOps.Application.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace GenAIOps.Infrastructure.Chat;

public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddFakeChatGateway(this IServiceCollection services)
    {
        services.AddSingleton<IChatGateway, FakeChatGateway>();
        return services;
    }

    public static IServiceCollection AddFoundryChatGateway(
        this IServiceCollection services,
        FoundryAgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<IChatGateway, FoundryAgentGateway>();
        return services;
    }
}
