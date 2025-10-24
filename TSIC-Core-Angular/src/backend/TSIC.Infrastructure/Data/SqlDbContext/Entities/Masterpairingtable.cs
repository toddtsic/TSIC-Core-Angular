using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class Masterpairingtable
{
    public int Id { get; set; }

    public int GNo { get; set; }

    public int GCnt { get; set; }

    public int TCnt { get; set; }

    public int Rnd { get; set; }

    public int T1 { get; set; }

    public int T2 { get; set; }
}
