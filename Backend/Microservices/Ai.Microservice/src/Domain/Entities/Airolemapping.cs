using System;
using System.Collections.Generic;

namespace Domain.Entities;

public partial class Airolemapping
{
    public int Aiid { get; set; }

    public int Roleid { get; set; }

    public DateTime? Assignedat { get; set; }

    public virtual Ai Ai { get; set; } = null!;

    public virtual Airole Role { get; set; } = null!;
}
