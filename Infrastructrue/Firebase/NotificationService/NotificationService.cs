using Application.Common.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using infrastructrue.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Firebase.NotificationService;

public class NotificationService(IFirebaseService firebaseService) : INotificationService
{
    
    public async Task<string> SendMessageAsync(string token, string title, string body)
    {
        var message = new Message()
        {
            Token = token,
            Notification = new Notification()
            {
                Title = title,
                Body = body
            }
        };
        var response = await firebaseService.FirebaseMessaging.SendAsync(message);
        return response;
    }
    public async Task SendMessagesAsync(IEnumerable<string> tokens, string title, string body)
    {
        var messages = new List<Message>();
        foreach (var token in tokens)
        {
            messages.Add(new Message()
            {
                Token = token,
                Notification = new Notification()
                {
                    Title = title,
                    Body = body
                }
            });
        }
        var response = await firebaseService.FirebaseMessaging.SendEachAsync(messages);
    }
}