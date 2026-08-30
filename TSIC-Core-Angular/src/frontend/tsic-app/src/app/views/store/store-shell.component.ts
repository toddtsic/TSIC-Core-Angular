import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { StoreService } from '../../infrastructure/services/store.service';

/** Which purchaser-facing store screen is being framed. */
export type StorePage = 'shop' | 'cart' | 'checkout' | 'history';

/**
 * The page frame shared by every PURCHASER-facing store screen.
 *
 * <p>Each of the four screens used to build its own: its own container class and width, its own
 * `.x-header` block defined separately in its own stylesheet, and its own idea of what belongs
 * in the top-right. Four implementations of one thing, which is how they came to disagree.</p>
 *
 * <p>The visible cost was navigation. The shelf carried a Cart button and a Purchase History
 * button; every screen after it replaced them with a single "Continue Shopping" link. So from
 * the cart there was no route to purchase history, from history none to the cart, and from
 * checkout neither — three dead ends you could only reverse out of.</p>
 *
 * <p>Width is the ONLY thing that varies, and it varies for a reason: the shelf wants room for
 * three tiles across, while the cart and checkout are forms and receipts that read worse wide.
 * That is a property of the page, so the shell derives it rather than accepting it.</p>
 *
 * <p>Title and icon are derived from `page` for the same reason — passing them in would let two
 * screens disagree about what the cart is called, which is the class of bug this exists to
 * remove.</p>
 */
@Component({
    selector: 'app-store-shell',
    standalone: true,
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './store-shell.component.html',
    styleUrl: './store-shell.component.scss',
})
export class StoreShellComponent {
    private readonly store = inject(StoreService);
    private readonly router = inject(Router);

    readonly page = input.required<StorePage>();

    readonly cartCount = this.store.cartCount;
    readonly purchaseCount = this.store.purchaseCount;

    private static readonly TITLES: Record<StorePage, { title: string; icon: string }> = {
        // "Store", not "Team Store" or "Event Store". Legacy calls it "Store"
        // (StoreFamily/CheckoutConfirmation.cshtml: "Return to Store") and so does the nav item
        // that gets you here. Two branded names were invented independently during the port and
        // sat one click apart — the shopper tapped "Store" and landed on "Event Store", signed
        // in, and arrived at "Team Store".
        shop: { title: 'Store', icon: 'bi-shop' },
        cart: { title: 'Your Cart', icon: 'bi-cart3' },
        checkout: { title: 'Checkout', icon: 'bi-credit-card' },
        history: { title: 'Purchase History', icon: 'bi-receipt' },
    };

    readonly title = computed(() => StoreShellComponent.TITLES[this.page()].title);
    readonly icon = computed(() => StoreShellComponent.TITLES[this.page()].icon);

    /**
     * The job path out of the current URL.
     *
     * <p>Links are built from it rather than written relative, because the store routes sit at
     * different segment depths — `store` is one segment and `store/cart` is two — so a relative
     * link that is correct on the shelf is wrong from the cart. A shared header cannot carry
     * four different prefixes.</p>
     *
     * <p>This is not the absolute-routerLink mistake the routing rule forbids. That rule exists
     * to stop `:jobPath` being dropped; these links carry the job path explicitly, read from the
     * URL the shopper is already on, so it survives by construction rather than by luck. Read
     * once: the shell is destroyed and rebuilt on every navigation between store screens.</p>
     */
    private readonly jobPath = this.router.url.split(/[?#]/)[0].split('/').filter(Boolean)[0] ?? '';

    readonly shopLink = ['/', this.jobPath, 'store'];
    readonly cartLink = ['/', this.jobPath, 'store', 'cart'];
    readonly historyLink = ['/', this.jobPath, 'store', 'invoices'];
}
