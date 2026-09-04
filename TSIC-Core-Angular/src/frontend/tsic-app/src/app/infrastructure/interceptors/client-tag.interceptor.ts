import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '@environments/environment';

/**
 * Stamps the reserved `?xc=` client tag on every API request.
 *
 * This is the web half of the X-Client rollout (rev 3). Without it the backend records
 * AppClientId 0 / PlatformId 0 / AppVersion '' for every row — not a defect, just the
 * "client did not identify itself" bucket, which is where ALL traffic sat until this
 * file existed.
 *
 * A query parameter and not a custom header (rev 3): a custom header makes an
 * otherwise-simple GET non-simple, so every anonymous request would buy a CORS
 * preflight round-trip. The same string in the URL carries identical information at
 * identical cost with no preflight, by construction.
 *
 * Format must match UsageClassifier.ParseClientTag on the server:
 *     name/version (platform)   e.g.  tsic-web/v260904.1149.a1b2c3d (web)
 *
 * `xc` is RESERVED — no endpoint may bind a parameter of that name. See
 * UsageClassifier.ClientTagQueryKey.
 */

const CLIENT_TAG_PARAM = 'xc';

/** Mirrors the server's SanitizeVersion: charset [0-9A-Za-z.-], illegal REPLACED
 *  with '-' (not deleted, so "1.2 beta" and "1.2-beta" stay distinguishable), capped
 *  at the column width. Done here as well as server-side so what we send is already
 *  what gets stored — a value that only becomes legal after the server rewrites it
 *  is a value we cannot search for in Seq. */
function sanitizeVersion(v: string): string {
	return v.replaceAll(/[^0-9A-Za-z.-]/g, '-').slice(0, 32);
}

/**
 * Built once at module load, not per request. It cannot change during a session:
 * buildVersion is compiled in at build time ('dev' under ng serve, the deploy stamp
 * otherwise).
 */
const CLIENT_TAG = `tsic-web/${sanitizeVersion(environment.buildVersion)} (web)`;

export const clientTagInterceptor: HttpInterceptorFn = (req, next) => {
	// API calls only. Static assets, help HTML and CDN images go to other origins that
	// neither read the tag nor should have their URLs varied by it (a varying query
	// string defeats caching on assets that are otherwise perfectly cacheable).
	if (!req.url.startsWith(environment.apiUrl)) {
		return next(req);
	}

	return next(req.clone({
		params: req.params.set(CLIENT_TAG_PARAM, CLIENT_TAG),
	}));
};
