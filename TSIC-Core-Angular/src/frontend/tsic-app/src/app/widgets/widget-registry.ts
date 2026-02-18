import { Type } from '@angular/core';

import { ClientBannerComponent } from '@widgets/layout/client-banner/client-banner.component';
import { BulletinsComponent } from '@widgets/communications/bulletins.component';
import { EventContactWidgetComponent } from '@widgets/event-info/event-contact-widget/event-contact-widget.component';
import { PlayerTrendWidgetComponent } from '@widgets/registration/player-trend-widget/player-trend-widget.component';
import { TeamTrendWidgetComponent } from '@widgets/registration/team-trend-widget/team-trend-widget.component';
import { AgegroupDistributionWidgetComponent } from '@widgets/registration/agegroup-distribution-widget/agegroup-distribution-widget.component';
import { YearOverYearWidgetComponent } from '@widgets/scheduling/year-over-year-widget/year-over-year-widget.component';

/**
 * Central widget registry: maps WidgetItemDto.componentKey → Angular component class.
 *
 * To add a new widget:
 *   1. Import the component
 *   2. Add one entry below
 *   Zero dashboard changes required.
 */
export const WIDGET_REGISTRY: Record<string, Type<unknown>> = {
	'client-banner':         ClientBannerComponent,
	'bulletins':             BulletinsComponent,
	'event-contact':         EventContactWidgetComponent,
	'player-trend-chart':    PlayerTrendWidgetComponent,
	'team-trend-chart':      TeamTrendWidgetComponent,
	'agegroup-distribution': AgegroupDistributionWidgetComponent,
	'year-over-year':        YearOverYearWidgetComponent,
};
