using System;
using System.Collections.Generic;

namespace Domain.Entities;

public partial class Airole
{
    public int Roleid { get; set; }

    public string Rolename { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<Airolemapping> Airolemappings { get; set; } = new List<Airolemapping>();
}
