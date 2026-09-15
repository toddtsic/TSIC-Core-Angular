import { HttpInterceptorFn } from '@angular/common/http';
import { SCHEDULE_PREVIEW_HEADER, isScheduleApiUrl, readSchedulePreviewToken } from '../services/schedule-preview-token';

/** Presents a held schedule-preview invite token on schedule API calls. See schedule-preview-token.ts. */
export const schedulePreviewInterceptor: HttpInterceptorFn = (req, next) => {
    const token = isScheduleApiUrl(req.url) ? readSchedulePreviewToken() : null;
    return next(token ? req.clone({ setHeaders: { [SCHEDULE_PREVIEW_HEADER]: token } }) : req);
};
