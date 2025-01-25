using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Interfaces;

public interface IApplicationDbContext
{
   
     DbSet<ChargingPointAttachment> ChargingPointAttachments { get; set; }

     DbSet<ChargingPlug> ChargingPlugs { get; set; }

     DbSet<ChargingPoint> ChargingPoints { get; set; }

     DbSet<ChargingPointType> ChargingPointTypes { get; set; }

     DbSet<PlugType> PlugTypes { get; set; }

     DbSet<Privilege> Privilages { get; set; }

     DbSet<Rate> Rates { get; set; }

     DbSet<Role> Roles { get; set; }

     DbSet<RolePrivlage> RolePrivlages { get; set; }

     DbSet<Status> Statuses { get; set; }

     DbSet<UserAccount> UserAccounts { get; set; }

     DbSet<UserComplaint> UserComplaints { get; set; }
     
     Task<int> SaveChanges(CancellationToken cancellationToken=default);

}