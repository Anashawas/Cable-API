using Application.Common.Interfaces;
using Cable.Core.Enums;
using FirebaseAdmin.Messaging;

namespace Infrastructrue.Firebase.NotificationService;

public class NotificationService(IFirebaseService firebaseService) : INotificationService
{
    public async Task<string> SendMessageAsync(
        string token,
        string title,
        string body,
        FirebaseAppType appType = FirebaseAppType.UserApp)
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

        var messaging = firebaseService.GetFirebaseMessaging(appType);
        var response = await messaging.SendAsync(message);
        return response;
    }

    public async Task<NotificationSendResult> SendMessagesAsync(
        IEnumerable<string> tokens,
        string title,
        string body,
        FirebaseAppType appType = FirebaseAppType.UserApp)
    {
        var tokenList = tokens.ToList();
        var messages = new List<Message>();

        foreach (var token in tokenList)
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

        var messaging = firebaseService.GetFirebaseMessaging(appType);
        var batchResponse = await messaging.SendEachAsync(messages);

        var result = new NotificationSendResult
        {
            TotalCount = tokenList.Count,
            SuccessCount = batchResponse.SuccessCount,
            FailureCount = batchResponse.FailureCount
        };

        for (int i = 0; i < batchResponse.Responses.Count; i++)
        {
            var response = batchResponse.Responses[i];
            var token = tokenList[i];

            if (response.IsSuccess)
            {
                result.SuccessfulTokens.Add(token);
            }
            else
            {
                result.FailedTokens.Add(token);
            }
        }

        return result;
    }
}