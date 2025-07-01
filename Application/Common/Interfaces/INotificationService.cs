namespace Application.Common.Interfaces;

public interface INotificationService
{
    Task<string> SendMessageAsync(string token, string title, string body);
    Task SendMessagesAsync(IEnumerable<string> tokens, string title, string body);
    
}