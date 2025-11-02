using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AllMCPSolution.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AllMCPSolution.Services
{
    public interface IUserNotificationService
    {
        Task NotifyBottleShareCreatedAsync(Guid recipientUserId, Guid bottleId, Guid sharedByUserId);
        Task NotifySisterhoodInvitationReceivedAsync(Guid invitedUserId, Guid sisterhoodId, string sisterhoodName);
        Task NotifySipSessionCreatedAsync(IEnumerable<Guid> userIds, Guid sipSessionId, string name, DateTime? scheduledAtUtc, DateTime? date);
        Task NotifySipSessionCreatedAsync(Guid userId, Guid sipSessionId, string name, DateTime? scheduledAtUtc, DateTime? date)
            => NotifySipSessionCreatedAsync(new[] { userId }, sipSessionId, name, scheduledAtUtc, date);
    }

    public class UserNotificationService : IUserNotificationService
    {
        private readonly IHubContext<NotificationsHub> _hub;

        public UserNotificationService(IHubContext<NotificationsHub> hub)
        {
            _hub = hub;
        }

        public Task NotifyBottleShareCreatedAsync(Guid recipientUserId, Guid bottleId, Guid sharedByUserId)
        {
            var payload = new
            {
                bottleId,
                sharedByUserId
            };
            return _hub.Clients.User(recipientUserId.ToString()).SendAsync("BottleShareCreated", payload);
        }

        public Task NotifySisterhoodInvitationReceivedAsync(Guid invitedUserId, Guid sisterhoodId, string sisterhoodName)
        {
            var payload = new
            {
                sisterhoodId,
                sisterhoodName
            };
            return _hub.Clients.User(invitedUserId.ToString()).SendAsync("SisterhoodInvitationReceived", payload);
        }

        public Task NotifySipSessionCreatedAsync(IEnumerable<Guid> userIds, Guid sipSessionId, string name, DateTime? scheduledAtUtc, DateTime? date)
        {
            var distinct = userIds?.Select(id => id.ToString()).Distinct().ToList() ?? new List<string>();
            if (distinct.Count == 0) return Task.CompletedTask;
            var payload = new
            {
                id = sipSessionId,
                title = name,
                scheduledAtUtc,
                date
            };
            return _hub.Clients.Users(distinct).SendAsync("SipSessionCreated", payload);
        }
    }
}
