# Legacy-Correction Processing-Fee — Affected Club Reps (for Todd + Ann to decide)

**Generated 2026-08-02 from dev TSICV5.** Lookup procedure + full explanation kept in memory (`legacy-correction-procfee-lookup`); **re-run against PROD at go-live**, scoped to migrating jobs.

## The situation
In **Legacy**, these club reps paid their team balance **in full via a Correction Record that stripped ALL processing fees**. The **new system correctly applies** proc fees, so on migration they show as **owing the proc fee** they already effectively settled. Anyone still genuinely owing balance is charged correctly — only these paid-in-full-minus-proc-fee reps are wrong.

**Signature:** active team where `owed_total ≈ fee_processing` **and** `paid_total ≈ fee_total − fee_processing` (base paid, only the proc fee outstanding). Canonical case: **Heather Printz / Fall Draw 2026** — Base $2000, ProcFee $35, Paid $2000, **Owed $35**.

## Totals (dev, all active teams, all years)
**77 affected club reps · 25 jobs · 8 customers · $4,804.94** total incorrectly-owed proc fees.
⚠️ Most rows are **old jobs (2022–2024)** — likely NOT migrating. **Current/near-term:** only `Top Threat Tournaments:Fall Draw 2026` (Christopher Biddle, Heather Printz) + a few 2025/26. **At go-live, restrict to the jobs being migrated.**

## ⚠️ Refine before writing anything off
The signature is the **fee-state outcome**, not proof of a Correction. Sanity-check: (1) **check payers** owe no proc fee by design — if any appear, they're a *correct* different state; (2) confirm the paying record is a **Correction** in the accounting for certainty. The 0.50 tolerance absorbs cent-level rounding.

---
# ✅ ACTIVE JOBS ONLY (not expired — `ExpiryUsers ≥ today`) — the set that matters for go-live

Only **2 club reps** on **1 active job** (Fall Draw 2026), **$70.00** total. Everything else in this file is on **expired/completed** jobs (2022–2024) that aren't migrating.

### Club Rep Summary — Active jobs
| Customer | Job | Club Rep | UserName | Affected Teams | Total Proc-Fee Owed |
|---|---|---|---|---:|---:|
| Top Threat Tournaments | Fall Draw 2026 | Christopher Biddle | cjb845 | 1 | $35.00 |
| Top Threat Tournaments | Fall Draw 2026 | Heather Printz | hprintz | 1 | $35.00 |
| | | **TOTAL** | | **2** | **$70.00** |

### Team Detail — Active jobs
| Customer | Job | Club Rep | UserName | Team | Proc-Fee Owed |
|---|---|---|---|---|---:|
| Top Threat Tournaments | Fall Draw 2026 | Christopher Biddle | cjb845 | Lady Reign 2029 Select | $35.00 |
| Top Threat Tournaments | Fall Draw 2026 | Heather Printz | hprintz | Mad Dog Shore Girls 2028 | $35.00 |

---
# 🗄️ ALL JOBS (incl. EXPIRED — reference only)
Full 169-team / 77-rep set across all years (mostly expired 2022–2024 jobs, not migrating). Kept for completeness / the go-live re-run.

## Full list — Customer | Job | Club Rep | UserName | Team | ProcFeeOwed
```
IWLCA | IWLCA:Capital Cup 2023 | Julie McLaren | blacklaxllc | Black Lax National 2025 | 51.80
IWLCA | IWLCA:Capital Cup 2023 | Kristen Mullady | kmullady | Lax Plus White OPEN | 51.80
IWLCA | IWLCA:Capital Cup 2023 | Mike Burdick | lestormelite@gmail.com | Lake Effect Storm Elite 2024 | 65.21
IWLCA | IWLCA:Capital Cup 2023 | Mike Farrell | Bisbee_K | TLC 2026 Gold | 40.60
IWLCA | IWLCA:Capital Cup 2023 | Patrick McAnulty | IWLCABluegrassLax | Bluegrass Premier 2024 | 40.60
IWLCA | IWLCA:Capital Cup 2023 | Richard Pound | RichardPound | Relentless Hustle 2025 | 51.80
IWLCA | IWLCA:Debut 2023 | Erin Abbot-Gillin | cclacrosse1 | 2027 Titanium | 7.00
IWLCA | IWLCA:Debut 2023 | Madii Fowler | madii_maas | True OH 2027 | 44.80
IWLCA | IWLCA:Presidents Cup 2023 | Brittney Fauss-Johnson | luckylax614 | Lucky Lax 25 | 63.00
IWLCA | IWLCA:Presidents Cup 2023 | Colleen Speth | SecondCity | Second City 2026 | 14.00
IWLCA | IWLCA:Presidents Cup 2023 | Kristi Awalt | Velocityohio | Velocity Elite | 51.80
IWLCA | IWLCA:Presidents Cup 2023 | Michael Brennan | IWLCALegacyLacrosseLI | Legacy GA | 56.00
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 25 Green | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 25 Pink | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 26 Pink | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 27 Green | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 27 Pink | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 28 Pink | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 29 Pink | 47.25
Lax For The Cure | Lax For The Cure:Fall Showcase 2023 | Drew White | Saltcitysnipers | 30 Pink | 47.25
Live Love Lax | Live Love Lax:Girls Youth Fall 2023 | Emily Ewin | eewin | 2029 | 50.72
Live Love Lax | Live Love Lax:Girls Youth Fall 2023 | Jillian Pfeifer | jill@team91lacrosse.com | 2030 Fury | 50.72
Long Island Elite Lacrosse | Long Island Elite Lacrosse:Prime Time Recruiting Showcase 2025 | John Ault | john@southernlaxevents.com | Shine Select 2027 Orange | 14.00
Long Island Elite Lacrosse | Long Island Elite Lacrosse:Prime Time Recruiting Showcase 2025 | John Ault | john@southernlaxevents.com | Shine Select 2029 Orange | 14.00
Long Island Elite Lacrosse | Long Island Elite Lacrosse:Prime Time Recruiting Showcase 2025 | John Ault | john@southernlaxevents.com | Shine Select 28 White | 14.00
Riot Lacrosse | Riot Lacrosse:Jersey Strong Showcase 2024 | Colleen Harris | cryan0974@yahoo.com | Varsity | 42.00
Riot Lacrosse | Riot Lacrosse:Jersey Strong Showcase 2024 | Heather Miller | Radolphlax | Randolph Rams | 42.00
Riot Lacrosse | Riot Lacrosse:Jersey Strong Showcase 2024 | Justin Boyd | jsbdds22 | Chatham NJ Boys Varsity | 42.00
Riot Lacrosse | Riot Lacrosse:Jersey Strong Showcase 2024 | Michael Taromina | mtaromina | Red | 42.00
Riot Lacrosse | Riot Lacrosse:Jersey Strong Showcase 2024 | Tara Spagnoletti | Dodgerslax | Madison Dodgers | 42.00
Shooting Stars Lacrosse | Shooting Stars Lacrosse:Spring Breakout 2023 | Bethesda Lacrosse | bethesdagirlslacrosse | BLC 2027 Blue | 12.22
Shooting Stars Lacrosse | Shooting Stars Lacrosse:Spring Breakout 2023 | Deanna Sandstrom | DBlood | Coppermine North 2030 | 20.97
Shooting Stars Lacrosse | Shooting Stars Lacrosse:Spring Breakout 2023 | Deanna Sandstrom | DBlood | Coppermine West 32/33 | 20.97
Top of the Bay Lacrosse | Top of the Bay Lacrosse:#LaxisLife 2026 | Trisha Ey | trishey22@gmail.com | FLC 2031 | 42.00
Top of the Bay Lacrosse | Top of the Bay Lacrosse:Fall Premier Showcase 2023 | Meghan Mcknelly | FCALacrosse | FCA 2029 | 28.00
Top Threat Tournaments | Top Threat Tournaments:Carolina Clash 2024 | Steve Olzark | solzark@xfiregroup.com | Carolina Force Elementary | 14.00
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Alison Limoncelli | revlacrosse | REV 2026 Blue | 47.25
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Alison Limoncelli | revlacrosse | REV 2027 Blue | 47.25
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2026 Rise | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2027 Bold | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2027 Grit | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2027 Rise | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2030 Bold | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2030 Rise | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Bernadette Pio | CLClacrosse | CLC 2031 Bold | 48.90
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Katie McMahon | ultimate | CC 2027 | 47.25
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Katie McMahon | ultimate | PA 2027 Blue | 14.19
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2023 | Katie McMahon | ultimate | PA 2027 White | 47.25
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2026 | Christopher Biddle | cjb845 | Lady Reign 2029 Select | 35.00
Top Threat Tournaments | Top Threat Tournaments:Fall Draw 2026 | Heather Printz | hprintz | Mad Dog Shore Girls 2028 | 35.00
Top Threat Tournaments | Top Threat Tournaments:Five Star 2024 | Dwayne Wilkins | Dwaynewilkins | DEWLAX 2033/2034 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Alan Clark | Yetilaxcoach | Blue | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Alan Clark | Yetilaxcoach | Silver | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Brian DiStefano | bdistefano | Myrtle Beach Lightning | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Cessna Manalili | tampabaysirens | Tampa Bay Sirens | 3.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Christine Wilson | RH Sand Gnats | Richmond Hill Sand Gnats | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Darryn Bahn | DarrynBahn | FL Keys Makos | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm Ashley | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm Cheryl | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm Chloe | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm Kerry | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm MS | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Orlando Storm Scott | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | John Ault | john@southernlaxevents.com | Tampa Storm MS | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kaitlin Sheridan | MnDOrl | 2023 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kaitlin Sheridan | MnDOrl | 2024 Red | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kaitlin Sheridan | MnDOrl | 2025 Red | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kaitlin Sheridan | MnDOrl | 2026 Red | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kaitlin Sheridan | MnDOrl | 2029/30 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kara Koolage | Beachlacrossejax | Thrive | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Kara Koolage | Beachlacrossejax | Thrive | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Dash Bolt- Open | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Dash Flash- Open | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Dash Gold- Open MS | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Dash Lightning- Open | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Dash Teal- Open MS | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Katie Cavanaugh | Duval Dash | Lady Dashers | 3.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | katie kastner | katiekastner | Flagler Firehawks | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Krista Grabher | FloridaShine | Florida Shine Elite Orange | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Krista Grabher | FloridaShine | Florida Shine Elite Suns | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Krista Grabher | FloridaShine | Florida Shine Elite Yellow | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Maddie Higgins | pipelinelax1@gmail.com | Pipeline 23 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Maddie Higgins | pipelinelax1@gmail.com | Pipeline 24 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Maddie Higgins | pipelinelax1@gmail.com | Pipeline 25 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Maddie Higgins | pipelinelax1@gmail.com | Pipeline 26 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Maddie Higgins | pipelinelax1@gmail.com | Pipeline MS | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Madelaine Higgins | info@pipelinelacrosse.org | Pipeline GeriHATrics | 3.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Mary Schwartz | molico17 | Myrtle Beach | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Meghan Braun | thelaxboxfl | Surge Lime | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Meghan Braun | thelaxboxfl | Surge Teal | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Michele Parr | Stjamessharks | Sharks | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Michele Parr | Stjamessharks | Inlet Wave | 3.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Patrick Timothee | sfexpress21 | South Florida Express Rose | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Patrick Timothee | sfexpress21 | South Florida Express Rose | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Selena Alvarado | ONLYFORCLUBREP | MAA Tigers | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Sophia Sesi | shocklax | Shock Lacrosse | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Tim Moltisanti | tmolt12 | Ohana Lacrosse | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Todd Beuer | Tbeuer936 | Space Coast Black 2023 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Todd Beuer | Tbeuer936 | Space Coast Green 2024 | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Todd Beuer | Tbeuer936 | Space Coast White | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Trey Burlingame | trey.burlingame@laxmaniax.co | Lax Maniax 2026 Black | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Trey Burlingame | trey.burlingame@laxmaniax.co | Lax Maniax Heat | 10.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Vanessa Tsarevich | vtsarevich | 10X | 3.50
Top Threat Tournaments | Top Threat Tournaments:Florida Wave 2022 | Zachary Beaverson | beaversz | STC Dawgs Girls Lacrosse Club | 10.50
Top Threat Tournaments | Top Threat Tournaments:Morgans Jam 2023 | caitlin copelan | soundlacrosse | 2028 Green | 43.75
Top Threat Tournaments | Top Threat Tournaments:Morgans Jam 2023 | caitlin copelan | soundlacrosse | 2031/32 White | 2.63
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | CC 2025 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | CC 2026 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | CC 2027 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | LV 2025 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | LV 2026 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | LV 2027 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | NJ 2026 Blue | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | NJ 2027 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | PA 2025 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | PA 2026 | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | PA 2027 Blue | 7.00
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2023 | Katie McMahon | ultimate | PA 2027 White | 43.75
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Becky Davis | nepaimpact@gmail.com | NEPA Impact 25/26 | 58.97
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Becky Davis | nepaimpact@gmail.com | NEPA Impact 27/28 | 13.31
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Becky Davis | nepaimpact@gmail.com | NEPA Impact 28/29 | 58.97
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Joey Sankey | Jsankey | Team 11 | 58.97
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Joey Sankey | Jsankey | Team 11 | 28.53
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Samantha Warner | Samanthawarner | 2033 Purple | 58.97
Top Threat Tournaments | Top Threat Tournaments:Platinum Games 2024 | Samantha Warner | Samanthawarner | 2034 North | 28.53
Top Threat Tournaments | Top Threat Tournaments:Pumpkin Smash 2025 | Brian Di Stefano | Bjdistefano | Myrtle Beach Lightning | 10.50
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Beth Schminke | Bschminke | Savannah Gulls | 35.00
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Brandon Allen | Branole | Georgia Outlaws 2025 Purple | 35.00
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Clare Boothe | southernzone | Southern Zone 2029 | 35.00
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Grace Goodman | MACLAX GA | HS 2026 | 35.00
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Jack Reynolds | palmettolacrossechs | 30/31 Blue | 10.50
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Krista Grabher | FloridaShine | Florida Shine Elite 2027 | 35.00
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2022 | Steve Olzark | solzark@xfiregroup.com | Carolina Force Elementary | 10.50
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Bridget Bendjy | info@carolinacoastlacrosse.com | Carolina Coast Lacrosse Black 2026/2025 | 29.75
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CAR 2027 | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CAR 2028/2029 | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CAR 2031/2032 | 12.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CAR 2033/2034 | 12.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2025 White | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2026 White | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2027 White | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2028 Blue | 12.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2028 White | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2029 Blue | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2030 Blue | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2030/2031 | 40.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2032 Blue | 12.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2033/2034 | 12.25
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2024 | Greg Allen | greg@laxtribe.com | Charlotte Fury Black | 58.91
Top Threat Tournaments | Top Threat Tournaments:Southern Lacrosse Showdown 2024 | Greg Allen | greg@laxtribe.com | Charlotte Fury Green | 25.09
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies HS | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies MS Blue | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies MS Green | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies MS White | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies Youth Blue | 15.75
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Ashley Petty | pettyash | Grizzlies Youth Green | 15.75
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Brandon Allen | Branole | Georgia Outlaws Pink | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Brandon Allen | Branole | Georgia Outlaws Purple | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Randy Qualls | Beastmodelqx | Beast Elite 2028 | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2022 | Randy Qualls | Beastmodelqx | Beast Elite 2029 | 40.25
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2025 White | 42.00
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2026 White | 38.50
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2027 White | 45.50
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2028 Blue | 45.50
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2029 Blue | 45.50
Top Threat Tournaments | Top Threat Tournaments:The Knockout 2023 | Katie McMahon | ultimate | ULTIMATE CLT 2030 Blue | 45.50
Top Threat Tournaments | Top Threat Tournaments:The SAT Georgia 2023 | Randy Qualls | Beastmodelqx | BEAST LAX 31 | 15.75
```

**Next:** Todd + Ann decide handling (e.g. zero out the residual proc-fee owed on these teams, or leave as-is). Re-run the lookup (memory: `legacy-correction-procfee-lookup`) against **PROD at go-live**, scoped to migrating jobs.
