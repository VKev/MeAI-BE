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

    public virtual DbSet<Post> Posts { get; set; }
    
    public virtual DbSet<Comment> Comments { get; set; }
    
    public virtual DbSet<Follow> Follows { get; set; }
    
    public virtual DbSet<Hashtag> Hashtags { get; set; }

    public virtual DbSet<PostHashtag> PostHashtags { get; set; }
    
    public virtual DbSet<PostLike> PostLikes { get; set; }
    
    public virtual DbSet<CommentLike> CommentLikes { get; set; }
    
    public virtual DbSet<Report> Reports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
