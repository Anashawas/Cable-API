using System.Reflection;
using Application.Common.Interfaces;
using Domain.Enitites;
using Infrastructrue.Common.Extensions;
using Infrastructrue.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Persistence;

public partial class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IEnumerable<SaveChangesInterceptor> saveChangesInterceptor,
    IOptions<DatabaseOptions> databaseSettingsOptions,
    IMediator mediator)
    : DbContext(options), IApplicationDbContext
{
    #region Memebers

    private readonly IEnumerable<SaveChangesInterceptor> _saveChangesInterceptors = saveChangesInterceptor;

    private readonly DatabaseOptions _databaseSettings = databaseSettingsOptions.Value;
    private readonly IMediator _mediator = mediator;

    #endregion

    #region Properties

    public DbSet<AttachmentChargerPoint> AttachmentChargerPoints { get; set; }
    public DbSet<ChargingPlug> ChargingPlugs { get; set; }
    public DbSet<ChargingPoint> ChargingPoints { get; set; }
    public DbSet<ChargingPointType> ChargingPointTypes { get; set; }
    public DbSet<PlugType> PlugTypes { get; set; }
    public DbSet<Privilege> Privilages { get; set; }
    public DbSet<Rate> Rates { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RolePrivlage> RolePrivlages { get; set; }
    public DbSet<Status> Statuses { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<UserComplaint> UserComplaints { get; set; }

    #endregion

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_saveChangesInterceptors);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(_databaseSettings.DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public async Task<int> SaveChanges(CancellationToken cancellationToken = default)
    {
        await _mediator.DispatchDomainEvents(this);
        return await SaveChangesAsync(cancellationToken);
    }
}