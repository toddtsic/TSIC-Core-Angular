using FluentAssertions;
using TSIC.Application.Services.Clubs;

namespace TSIC.Tests.TeamRegistration;

/// <summary>
/// CLUB NAME MATCHING TESTS
///
/// Validates the fuzzy matching engine that prevents duplicate clubs during
/// club rep registration. Duplicate clubs fragment the team library --
/// teams' win-loss records and event history get split across two records
/// instead of staying with one club.
///
/// Three matching strategies are tested:
///   1. Normalization -- abbreviations, misspellings, filler words
///   2. Levenshtein distance -- catches typos ("Charlote" vs "Charlotte")
///   3. Token/Jaccard similarity -- catches word reordering ("Baltimore Lax" vs "Lax Baltimore")
///   4. Mega-club detection -- same root org across states ("3 Point - VA" / "3 Point - NC")
///
/// The composite score (max of Levenshtein and Jaccard) determines whether
/// registration is blocked (85%+), warned (65-84%), or allowed (below 65%).
/// </summary>
public class ClubNameMatcherTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  NORMALIZATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club name uses common abbreviation "LC" for "Lacrosse Club"
    /// EXPECTED: Expansion is whole-token only -- "LC" becomes "lacrosse club"
    ///           but "falcon" is NOT mangled (old bug: substring "lc" in "falcon")
    /// </summary>
    [Fact(DisplayName = "Normalize: 'Charlotte LC' expands abbreviation then strips sport/org filler")]
    public void Normalize_ExpandsLcAbbreviation()
    {
        // "LC" expands to "lacrosse club", then both are stripped as filler
        ClubNameMatcher.NormalizeClubName("Charlotte LC")
            .Should().Be("charlotte");
    }

    /// <summary>
    /// SCENARIO: Club name contains "falcon" which has "lc" as a substring
    /// EXPECTED: "falcon" is preserved -- abbreviation expansion is whole-token only
    /// </summary>
    [Fact(DisplayName = "Normalize: 'Falcons LC' preserves 'falcons', strips expanded sport filler")]
    public void Normalize_DoesNotMangleFalcons()
    {
        var result = ClubNameMatcher.NormalizeClubName("Falcons LC");
        result.Should().Contain("falcons", "word 'falcons' must not be mangled by 'lc' expansion");
        result.Should().Be("falcons", "expanded 'lacrosse club' stripped as filler");
    }

    /// <summary>
    /// SCENARIO: Club name uses multiple abbreviations
    /// EXPECTED: Each is expanded independently at word boundaries
    /// </summary>
    [Fact(DisplayName = "Normalize: 'N Co Lax' becomes 'north county' (sport stripped)")]
    public void Normalize_MultipleAbbreviations()
    {
        ClubNameMatcher.NormalizeClubName("N Co Lax")
            .Should().Be("north county");
    }

    /// <summary>
    /// SCENARIO: Club name has common misspelling of "lacrosse"
    /// EXPECTED: Corrected before matching so typos don't cause false negatives
    /// </summary>
    [Fact(DisplayName = "Normalize: misspelling 'Lacrose' corrected then stripped as sport filler")]
    public void Normalize_FixesMisspelling()
    {
        // "Lacrose" corrected to "lacrosse", then stripped as sport filler
        ClubNameMatcher.NormalizeClubName("Charlotte Lacrose")
            .Should().Be("charlotte");
    }

    /// <summary>
    /// SCENARIO: Club name includes filler words "The" and "of"
    /// EXPECTED: Filler words removed -- they add noise to similarity scoring
    /// </summary>
    [Fact(DisplayName = "Normalize: strips filler words ('The', 'of')")]
    public void Normalize_StripsFiller()
    {
        ClubNameMatcher.NormalizeClubName("The Lions of Charlotte")
            .Should().Be("lions charlotte");
    }

    /// <summary>
    /// SCENARIO: Club name has punctuation and extra whitespace
    /// EXPECTED: Cleaned to letters/digits/single-spaces only
    /// </summary>
    [Fact(DisplayName = "Normalize: strips punctuation, collapses whitespace, strips sport/org filler")]
    public void Normalize_StripsPunctuation()
    {
        // "F.C." → "fc" → "football club" → both stripped; "Youth" → stripped
        ClubNameMatcher.NormalizeClubName("Charlotte  F.C.  (Youth)")
            .Should().Be("charlotte");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LEVENSHTEIN SIMILARITY (typos, minor variations)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Identical club names after normalization
    /// EXPECTED: 100% match
    /// </summary>
    [Fact(DisplayName = "Levenshtein: identical names score 100%")]
    public void Levenshtein_IdenticalNames()
    {
        ClubNameMatcher.CalculateSimilarity("charlotte fury", "charlotte fury")
            .Should().Be(100);
    }

    /// <summary>
    /// SCENARIO: Single character typo ("Charlote" vs "Charlotte")
    /// EXPECTED: Very high score (90%+) -- this should trigger Tier 1 block
    /// </summary>
    [Fact(DisplayName = "Levenshtein: single-char typo scores 90%+")]
    public void Levenshtein_SingleCharTypo()
    {
        ClubNameMatcher.CalculateSimilarity("charlote fury", "charlotte fury")
            .Should().BeGreaterThanOrEqualTo(90);
    }

    /// <summary>
    /// SCENARIO: Completely different names
    /// EXPECTED: Low score (well below 65%)
    /// </summary>
    [Fact(DisplayName = "Levenshtein: unrelated names score below 50%")]
    public void Levenshtein_UnrelatedNames()
    {
        ClubNameMatcher.CalculateSimilarity("charlotte fury", "baltimore crabs")
            .Should().BeLessThan(50);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TOKEN/JACCARD SIMILARITY (word reordering)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Same words in different order ("Baltimore Lacrosse" vs "Lacrosse Baltimore")
    /// EXPECTED: 100% token match -- Levenshtein alone would score low here
    /// </summary>
    [Fact(DisplayName = "Token: reordered words score 100%")]
    public void Token_ReorderedWords()
    {
        // These are pre-normalized inputs passed directly to token similarity
        ClubNameMatcher.CalculateTokenSimilarity("baltimore fury", "fury baltimore")
            .Should().Be(100);
    }

    /// <summary>
    /// SCENARIO: One word different ("Charlotte Fury" vs "Charlotte Thunder")
    /// EXPECTED: Partial overlap (50% -- 1 of 2 unique words match)
    /// </summary>
    [Fact(DisplayName = "Token: partial word overlap scores proportionally")]
    public void Token_PartialOverlap()
    {
        var score = ClubNameMatcher.CalculateTokenSimilarity("charlotte fury", "charlotte thunder");
        score.Should().BeGreaterThanOrEqualTo(30).And.BeLessThan(70);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMPOSITE SCORE (max of both engines)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Word-reordered name that Levenshtein alone would miss
    /// EXPECTED: Composite score catches it via token matching (85%+ = Tier 1 block)
    /// WHY IT MATTERS: A club rep typing "Lacrosse Baltimore" when "Baltimore Lacrosse"
    ///   exists should be blocked -- it's the same club
    /// </summary>
    [Fact(DisplayName = "Composite: word reorder caught even though Levenshtein is low")]
    public void Composite_WordReorderCaught()
    {
        ClubNameMatcher.CalculateCompositeScore("Baltimore Lacrosse", "Lacrosse Baltimore")
            .Should().BeGreaterThanOrEqualTo(85, "token engine should catch word reordering");
    }

    /// <summary>
    /// SCENARIO: Abbreviation + typo combination ("Charlote Lax" vs "Charlotte Lacrosse")
    /// EXPECTED: After normalization both become similar -- should score in warning+ range
    /// </summary>
    [Fact(DisplayName = "Composite: abbreviation + typo still scores high after normalization")]
    public void Composite_AbbreviationPlusTypo()
    {
        ClubNameMatcher.CalculateCompositeScore("Charlote Lax", "Charlotte Lacrosse")
            .Should().BeGreaterThanOrEqualTo(75, "normalization expands 'Lax' and fixes typo proximity");
    }

    /// <summary>
    /// SCENARIO: Truly different clubs with no relation
    /// EXPECTED: Below 65% -- no friction during registration
    /// </summary>
    [Fact(DisplayName = "Composite: unrelated clubs score below 65%")]
    public void Composite_UnrelatedClubs()
    {
        ClubNameMatcher.CalculateCompositeScore("Charlotte Fury", "Baltimore Crabs")
            .Should().BeLessThan(65, "different clubs should not trigger any gate");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MEGA-CLUB ROOT EXTRACTION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club name with "dash state" pattern ("3 Point Lacrosse - VA")
    /// EXPECTED: Root extracted as "3 Point Lacrosse" (location suffix stripped)
    /// </summary>
    [Fact(DisplayName = "Root: '3 Point Lacrosse - VA' extracts '3 Point Lacrosse'")]
    public void Root_DashStatePattern()
    {
        ClubNameMatcher.ExtractClubRoot("3 Point Lacrosse - VA")
            .Should().Be("3 Point Lacrosse");
    }

    /// <summary>
    /// SCENARIO: Club name with parenthesized state ("Crabs (MD)")
    /// EXPECTED: Root extracted without the state suffix
    /// </summary>
    [Fact(DisplayName = "Root: 'Crabs (MD)' extracts 'Crabs'")]
    public void Root_ParenStatePattern()
    {
        ClubNameMatcher.ExtractClubRoot("Crabs (MD)")
            .Should().Be("Crabs");
    }

    /// <summary>
    /// SCENARIO: Club name with no location suffix
    /// EXPECTED: Returns null -- this is not a location-qualified name
    /// </summary>
    [Fact(DisplayName = "Root: 'Charlotte Fury' returns null (no location suffix)")]
    public void Root_NoSuffix()
    {
        ClubNameMatcher.ExtractClubRoot("Charlotte Fury")
            .Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MEGA-CLUB RELATIONSHIP DETECTION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Two branches of the same organization ("3 Point - VA" and "3 Point - NC")
    /// EXPECTED: Detected as related clubs -- both have location suffixes and roots match
    /// WHY IT MATTERS: These are legitimate different clubs (different state) but the system
    ///   should flag them as "same organization" so the rep knows what they're looking at
    /// </summary>
    [Fact(DisplayName = "Related: same org different states detected as related")]
    public void Related_SameOrgDifferentStates()
    {
        ClubNameMatcher.AreRelatedClubs("3 Point Lacrosse - VA", "3 Point Lacrosse - NC")
            .Should().BeTrue();
    }

    /// <summary>
    /// SCENARIO: Two unrelated clubs that both happen to have location suffixes
    /// EXPECTED: NOT related -- different root names
    /// </summary>
    [Fact(DisplayName = "Related: different orgs with state suffixes are NOT related")]
    public void Related_DifferentOrgsNotRelated()
    {
        ClubNameMatcher.AreRelatedClubs("Charlotte Fury - VA", "Baltimore Crabs - MD")
            .Should().BeFalse();
    }

    /// <summary>
    /// SCENARIO: Two clubs without location suffixes
    /// EXPECTED: NOT related -- mega-club detection requires location suffixes on both
    /// </summary>
    [Fact(DisplayName = "Related: clubs without state suffixes are not flagged as related")]
    public void Related_NoSuffixes()
    {
        ClubNameMatcher.AreRelatedClubs("Charlotte Fury", "Charlotte Thunder")
            .Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TIER BOUNDARY TESTS (verify thresholds used by registration gate)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Exact same club name
    /// EXPECTED: Scores in Tier 1 range (85%+) -- registration should be BLOCKED
    /// WHY IT MATTERS: This is the hijacking scenario -- someone types the exact
    ///   name of an existing club to gain access to its team library
    /// </summary>
    [Fact(DisplayName = "Tier boundary: exact name match is in BLOCK range (85%+)")]
    public void TierBoundary_ExactMatch_IsBlocked()
    {
        ClubNameMatcher.CalculateCompositeScore("Charlotte Fury", "Charlotte Fury")
            .Should().BeGreaterThanOrEqualTo(85);
    }

    /// <summary>
    /// SCENARIO: Name with an "LC" (Lacrosse Club) suffix ("Charlotte Fury" vs "Charlotte Fury LC")
    /// EXPECTED: "LC" expands to "lacrosse club"; both are filler words, so after
    ///   normalization both sides reduce to "charlotte fury" — identical → 100.
    /// WHY IT MATTERS: Distinct chapters of a mega-club use STATE suffixes ("Aacme - CA",
    ///   "Aacme - NC") and are handled by mega-club root extraction. An "LC" suffix is
    ///   industry boilerplate, not a chapter marker — every lacrosse club could write
    ///   itself with or without it. Treating the two forms as different would split
    ///   team libraries and win-loss records across duplicate club records.
    /// </summary>
    [Fact(DisplayName = "Tier boundary: 'LC' suffix is filler — same club, hard block")]
    public void TierBoundary_WithSuffix_BlocksAsDuplicate()
    {
        ClubNameMatcher.CalculateCompositeScore("Charlotte Fury", "Charlotte Fury LC")
            .Should().BeGreaterThanOrEqualTo(85, "'LC' is sport-industry boilerplate — this is the same club");
    }

    /// <summary>
    /// SCENARIO: Similar but clearly different clubs sharing a city name
    /// EXPECTED: Below 85% -- should NOT block, but may warn
    /// </summary>
    [Fact(DisplayName = "Tier boundary: same-city different-name is NOT in BLOCK range")]
    public void TierBoundary_SameCityDifferentName_NotBlocked()
    {
        ClubNameMatcher.CalculateCompositeScore("Charlotte Fury", "Charlotte Thunder")
            .Should().BeLessThan(85, "sharing a city name alone should not block registration");
    }

    /// <summary>
    /// SCENARIO: New club "Aacme Lax" should NOT match "Arc Lacrosse" just because
    ///           both contain the word "Lacrosse" (or its abbreviation "Lax").
    /// EXPECTED: Below 65% — sport names are stripped as filler, leaving only the
    ///           distinctive names ("aacme" vs "arc") which are clearly different.
    /// WHY IT MATTERS: Without sport-word stripping, every lacrosse club matched
    ///           every other lacrosse club at 50%+ via Jaccard token overlap.
    /// </summary>
    [Fact(DisplayName = "Sport filler: 'Aacme Lax' does NOT match 'Arc Lacrosse'")]
    public void SportFiller_DifferentClubsSharingSportName_NoMatch()
    {
        ClubNameMatcher.CalculateCompositeScore("Aacme Lax", "Arc Lacrosse")
            .Should().BeLessThan(65, "sport name alone should not cause a match");
    }
}
