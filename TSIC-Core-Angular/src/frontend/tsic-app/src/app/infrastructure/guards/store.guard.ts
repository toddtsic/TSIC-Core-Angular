import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { isStoreEligible } from '../constants/roles.constants';

/**
 * Store access guard enforcing three entry modes via route data `storeMode`:
 *
 * - 'walk-up'  — Kiosk mode: force logout if authenticated, then allow (clean slate)
 * - 'login'    — Store login page: redirect to catalog if already store-eligible;
 *                logout non-eligible sessions so the login form starts fresh
 * - (default)  — Catalog / cart / checkout: require authenticated + store-eligible role
 *                (Player or Family). Redirects others to store/login.
 */
export const storeGuard: CanActivateFn = (route, state) => {
	const auth = inject(AuthService);
	const router = inject(Router);

	const storeMode = route.data['storeMode'] as string | undefined;
	const user = auth.getCurrentUser();
	const isAuth = auth.isAuthenticated();

	// Resolve jobPath from route hierarchy (same fallback pattern as authGuard)
	let jobPath = route.paramMap.get('jobPath') || route.parent?.paramMap.get('jobPath');
	if (!jobPath && state.url) {
		const match = state.url.match(/^\/([a-z0-9-]{3,40})(\/|$|\?)/);
		if (match) jobPath = match[1];
	}
	jobPath = jobPath || 'tsic';

	// ── Walk-up (kiosk): always start with a clean slate ──
	if (storeMode === 'walk-up') {
		if (isAuth) {
			auth.logoutLocal();
		}
		return true;
	}

	// ── Store login page ──
	if (storeMode === 'login') {
		// Already store-eligible → skip login, go straight to catalog
		if (isAuth && user && isStoreEligible(user.role)) {
			return router.createUrlTree([`/${jobPath}/store`]);
		}
		// Authenticated but wrong role → clear session so login form starts fresh
		if (isAuth && user && !isStoreEligible(user.role)) {
			auth.logoutLocal();
		}
		return true;
	}

	// ── Default: catalog / item / cart / checkout ──
	if (!isAuth || !user) {
		return router.createUrlTree([`/${jobPath}/store/login`], {
			queryParams: { returnUrl: state.url },
		});
	}

	if (!isStoreEligible(user.role)) {
		auth.logoutLocal();
		return router.createUrlTree([`/${jobPath}/store/login`], {
			queryParams: { returnUrl: state.url },
		});
	}

	return true;
};
