using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    public virtual DbSet<Config> Configs { get; set; }

    public virtual DbSet<Resource> Resources { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<UserSubscription> UserSubscriptions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Config>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("configs_pkey");

            entity.ToTable("configs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChatModel).HasColumnName("chat_model");
            entity.Property(e => e.MediaAspectRatio).HasColumnName("media_aspect_ratio");
            entity.Property(e => e.NumberOfVariances).HasColumnName("number_of_variances");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<Resource>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("resources_pkey");

            entity.ToTable("resources");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Link).HasColumnName("link").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.ResourceType).HasColumnName("type");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.User).WithMany(p => p.Resources)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("resources_user_id_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Name, "roles_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

            entity.ToTable("subscriptions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.MeAiCoin).HasColumnName("me_ai_coin").HasColumnType("numeric(18,2)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            var limitsJsonOptions = new JsonSerializerOptions();
            var limitsComparer = new ValueComparer<SubscriptionLimits?>(
                (left, right) => SubscriptionLimitsEquals(left, right),
                value => SubscriptionLimitsHashCode(value),
                value => SubscriptionLimitsSnapshot(value));

            entity.Property(e => e.Limits)
                .HasColumnName("limits")
                .HasColumnType("jsonb")
                .HasConversion(
                    limits => limits == null ? null : JsonSerializer.Serialize(limits, limitsJsonOptions),
                    json => string.IsNullOrWhiteSpace(json)
                        ? null
                        : JsonSerializer.Deserialize<SubscriptionLimits>(json, limitsJsonOptions))
                .Metadata.SetValueComparer(limitsComparer);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("transactions_pkey");

            entity.ToTable("transactions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RelationId).HasColumnName("relation_id");
            entity.Property(e => e.RelationType).HasColumnName("relation_type");
            entity.Property(e => e.Cost).HasColumnName("cost").HasColumnType("numeric(18,2)");
            entity.Property(e => e.TransactionType).HasColumnName("type");
            entity.Property(e => e.TokenUsed).HasColumnName("token_used");
            entity.Property(e => e.PaymentMethod).HasColumnName("payment_method");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.User).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("transactions_user_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Username, "users_username_key").IsUnique();
            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").IsRequired();
            entity.Property(e => e.FullName).HasColumnName("full_name");
            entity.Property(e => e.Birthday).HasColumnName("birthday").HasColumnType("date");
            entity.Property(e => e.PhoneNumber).HasColumnName("phone_number");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.AvatarResourceId).HasColumnName("avatar_resource_id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.MeAiCoin)
                .HasColumnName("me_ai_coin")
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_roles_pkey");

            entity.ToTable("user_roles");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("user_roles_role_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_roles_user_id_fkey");
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_subscriptions_pkey");

            entity.ToTable("user_subscriptions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
            entity.Property(e => e.ActiveDate).HasColumnName("active_date").HasColumnType("timestamp with time zone");
            entity.Property(e => e.EndDate).HasColumnName("end_date").HasColumnType("timestamp with time zone");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");

            entity.HasOne(d => d.User).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_subscriptions_user_id_fkey");

            entity.HasOne(d => d.Subscription).WithMany(p => p.UserSubscriptions)
                .HasForeignKey(d => d.SubscriptionId)
                .HasConstraintName("user_subscriptions_subscription_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    private static bool SubscriptionLimitsEquals(SubscriptionLimits? left, SubscriptionLimits? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.NumberOfSocialAccounts == right.NumberOfSocialAccounts
            && left.RateLimitForContentCreation == right.RateLimitForContentCreation
            && left.NumberOfWorkspaces == right.NumberOfWorkspaces;
    }

    private static int SubscriptionLimitsHashCode(SubscriptionLimits? value)
    {
        if (value is null)
        {
            return 0;
        }

        return HashCode.Combine(
            value.NumberOfSocialAccounts,
            value.RateLimitForContentCreation,
            value.NumberOfWorkspaces);
    }

    private static SubscriptionLimits? SubscriptionLimitsSnapshot(SubscriptionLimits? value)
    {
        if (value is null)
        {
            return null;
        }

        return new SubscriptionLimits
        {
            NumberOfSocialAccounts = value.NumberOfSocialAccounts,
            RateLimitForContentCreation = value.RateLimitForContentCreation,
            NumberOfWorkspaces = value.NumberOfWorkspaces
        };
    }
}
