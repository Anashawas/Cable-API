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
        FirebaseAppType appType = FirebaseAppType.UserApp,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var message = new Message()
        {
            Token = token,
            Notification = new Notification()
            {
                Title = title,
                Body = body
            },
            Data = data
        };

        var messaging = firebaseService.GetFirebaseMessaging(appType);
        var response = await messaging.SendAsync(message);
        return response;
    }

    // FirebaseAdmin SDK enforces a hard cap of 500 messages per SendEachAsync call.
    // We chunk larger token lists into ≤500-message batches and aggregate the results.
    private const int FcmMaxBatchSize = 500;

    public async Task<NotificationSendResult> SendMessagesAsync(
        IEnumerable<string> tokens,
        string title,
        string body,
        FirebaseAppType appType = FirebaseAppType.UserApp,
        IReadOnlyDictionary<string, string>? data = null)
    {
        var tokenList = tokens?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
                        ?? new List<string>();

        var result = new NotificationSendResult { TotalCount = tokenList.Count };

        if (tokenList.Count == 0)
        {
            return result;
        }

        var messaging = firebaseService.GetFirebaseMessaging(appType);

        for (var offset = 0; offset < tokenList.Count; offset += FcmMaxBatchSize)
        {
            var chunk = tokenList
                .Skip(offset)
                .Take(FcmMaxBatchSize)
                .ToList();

            var messages = chunk
                .Select(token => new Message
                {
                    Token = token,
                    Notification = new Notification
                    {
                        Title = title,
                        Body  = body
                    },
                    Data = data
                })
                .ToList();

            var batchResponse = await messaging.SendEachAsync(messages);

            result.SuccessCount += batchResponse.SuccessCount;
            result.FailureCount += batchResponse.FailureCount;

            for (var i = 0; i < batchResponse.Responses.Count; i++)
            {
                var token = chunk[i];
                if (batchResponse.Responses[i].IsSuccess)
                    result.SuccessfulTokens.Add(token);
                else
                    result.FailedTokens.Add(token);
            }
        }

        return result;
    }
}