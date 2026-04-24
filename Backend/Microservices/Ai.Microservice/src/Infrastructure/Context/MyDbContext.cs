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

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<ChatSession> ChatSessions { get; set; }

    public virtual DbSet<PostBuilder> PostBuilders { get; set; }

    public virtual DbSet<Post> Posts { get; set; }

    public virtual DbSet<PostResource> PostResources { get; set; }

    public virtual DbSet<PostPublication> PostPublications { get; set; }

    public virtual DbSet<PostMetricSnapshot> PostMetricSnapshots { get; set; }

    public virtual DbSet<VideoTask> VideoTasks { get; set; }

    public virtual DbSet<ImageTask> ImageTasks { get; set; }

    public virtual DbSet<CoinPricingCatalogEntry> CoinPricingCatalog { get; set; }

    public virtual DbSet<AiSpendRecord> AiSpendRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
