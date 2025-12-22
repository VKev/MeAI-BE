using System;
using System.Collections.Generic;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Context;

public partial class MyDbContext : DbContext
{
    public MyDbContext()
    {
    }

    public MyDbContext(DbContextOptions<MyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Ai> Ais { get; set; }

    public virtual DbSet<Airole> Airoles { get; set; }

    public virtual DbSet<Airolemapping> Airolemappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ai>(entity =>
        {
            entity.HasKey(e => e.Aiid).HasName("ai_pkey");

            entity.ToTable("ai");

            entity.HasIndex(e => e.Email, "ai_email_key").IsUnique();

            entity.Property(e => e.Aiid).HasColumnName("aiid");
            entity.Property(e => e.Createdat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("createdat");
            entity.Property(e => e.Email)
                .HasMaxLength(150)
                .HasColumnName("email");
            entity.Property(e => e.Fullname)
                .HasMaxLength(100)
                .HasColumnName("fullname");
            entity.Property(e => e.Phonenumber)
                .HasMaxLength(15)
                .HasColumnName("phonenumber");

            entity.Navigation(e => e.Airolemappings).HasField("_airolemappings");
        });

        modelBuilder.Entity<Airole>(entity =>
        {
            entity.HasKey(e => e.Roleid).HasName("airole_pkey");

            entity.ToTable("airole");

            entity.HasIndex(e => e.Rolename, "airole_rolename_key").IsUnique();

            entity.Property(e => e.Roleid).HasColumnName("roleid");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Rolename)
                .HasMaxLength(50)
                .HasColumnName("rolename");
        });

        modelBuilder.Entity<Airolemapping>(entity =>
        {
            entity.HasKey(e => new { e.Aiid, e.Roleid }).HasName("airolemapping_pkey");

            entity.ToTable("airolemapping");

            entity.Property(e => e.Aiid).HasColumnName("aiid");
            entity.Property(e => e.Roleid).HasColumnName("roleid");
            entity.Property(e => e.Assignedat)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("assignedat");

            entity.HasOne(d => d.Ai).WithMany(p => p.Airolemappings)
                .HasForeignKey(d => d.Aiid)
                .HasConstraintName("airolemapping_aiid_fkey");

            entity.HasOne(d => d.Role).WithMany(p => p.Airolemappings)
                .HasForeignKey(d => d.Roleid)
                .HasConstraintName("airolemapping_roleid_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
