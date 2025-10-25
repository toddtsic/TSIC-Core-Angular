using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobDisplayOptions
{
    public Guid JobId { get; set; }

    public string? ParallaxBackgroundImage { get; set; }

    public int ParallaxSlideCount { get; set; }

    public string? ParallaxSlide1Image { get; set; }

    public string? ParallaxSlide1Text1 { get; set; }

    public string? ParallaxSlide1Text2 { get; set; }

    public string? ParallaxSlide2Image { get; set; }

    public string? ParallaxSlide2Text1 { get; set; }

    public string? ParallaxSlide2Text2 { get; set; }

    public string? ParallaxSlide3Image { get; set; }

    public string? ParallaxSlide3Text1 { get; set; }

    public string? ParallaxSlide3Text2 { get; set; }

    public string? LogoHeader { get; set; }

    public string? LogoFooter { get; set; }

    public string? BlockRecentWorks { get; set; }

    public string? BlockRecentImage1 { get; set; }

    public string? BlockRecentImage2 { get; set; }

    public string? BlockRecentImage3 { get; set; }

    public string? BlockRecentImage4 { get; set; }

    public string? BlockPurchase { get; set; }

    public string? BlockService { get; set; }

    public string LebUserId { get; set; } = null!;

    public DateTime Modified { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers LebUser { get; set; } = null!;
}
