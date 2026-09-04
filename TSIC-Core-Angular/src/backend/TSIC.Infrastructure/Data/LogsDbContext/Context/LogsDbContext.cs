using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TSIC.Domain.LogEntities;

namespace TSIC.Infrastructure.Data.LogsDbContext;

public partial class LogsDbContext : DbContext
{
    public LogsDbContext(DbContextOptions<LogsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AppClients> AppClients { get; set; }

    public virtual DbSet<AppUsage> AppUsage { get; set; }

    public virtual DbSet<Browsers> Browsers { get; set; }

    public virtual DbSet<DeviceClasses> DeviceClasses { get; set; }

    public virtual DbSet<Platforms> Platforms { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppClients>(entity =>
        {
            entity.HasKey(e => e.AppClientId);

            entity.ToTable("AppClients", "logs");

            entity.HasIndex(e => e.AppClientName, "UQ_AppClients_Name").IsUnique();

            entity.Property(e => e.AppClientId).ValueGeneratedNever();
            entity.Property(e => e.AppClientName)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<AppUsage>(entity =>
        {
            entity.ToTable("AppUsage", "logs");

            entity.HasIndex(e => new { e.JobId, e.OccurredAt }, "IX_AppUsage_JobId_OccurredAt");

            entity.HasIndex(e => e.OccurredAt, "IX_AppUsage_OccurredAt");

            entity.Property(e => e.Action)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.AppVersion)
                .HasMaxLength(32)
                .IsUnicode(false);
            entity.Property(e => e.Controller)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.OccurredAt).HasPrecision(3);
            entity.Property(e => e.QueryString).HasMaxLength(400);
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.AppClient).WithMany(p => p.AppUsage)
                .HasForeignKey(d => d.AppClientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AppUsage_AppClients");

            entity.HasOne(d => d.Browser).WithMany(p => p.AppUsage)
                .HasForeignKey(d => d.BrowserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AppUsage_Browsers");

            entity.HasOne(d => d.DeviceClass).WithMany(p => p.AppUsage)
                .HasForeignKey(d => d.DeviceClassId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AppUsage_DeviceClasses");

            entity.HasOne(d => d.Platform).WithMany(p => p.AppUsage)
                .HasForeignKey(d => d.PlatformId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_AppUsage_Platforms");
        });

        modelBuilder.Entity<Browsers>(entity =>
        {
            entity.HasKey(e => e.BrowserId);

            entity.ToTable("Browsers", "logs");

            entity.HasIndex(e => e.BrowserName, "UQ_Browsers_Name").IsUnique();

            entity.Property(e => e.BrowserId).ValueGeneratedNever();
            entity.Property(e => e.BrowserName)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<DeviceClasses>(entity =>
        {
            entity.HasKey(e => e.DeviceClassId);

            entity.ToTable("DeviceClasses", "logs");

            entity.HasIndex(e => e.DeviceClassName, "UQ_DeviceClasses_Name").IsUnique();

            entity.Property(e => e.DeviceClassId).ValueGeneratedNever();
            entity.Property(e => e.DeviceClassName)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Platforms>(entity =>
        {
            entity.HasKey(e => e.PlatformId);

            entity.ToTable("Platforms", "logs");

            entity.HasIndex(e => e.PlatformName, "UQ_Platforms_Name").IsUnique();

            entity.Property(e => e.PlatformId).ValueGeneratedNever();
            entity.Property(e => e.PlatformName)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
