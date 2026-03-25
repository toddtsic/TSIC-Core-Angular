/** Pre-built email templates for the fly-in detail panel */

export interface EmailTemplate {
  label: string;
  subject: string;
  body: string;
}

export interface EmailTemplateCategory {
  category: string;
  requiresSubscription?: boolean;
  templates: EmailTemplate[];
}

/**
 * Templates adapted from ARB Health dashboard (arb-health.component.ts).
 * Tokens use batch-email-compatible names (!PERSON, !AMTOWED, etc.)
 * so they resolve through the standard TextSubstitutionService pipeline.
 */
export const EMAIL_TEMPLATE_CATEGORIES: EmailTemplateCategory[] = [
  {
    category: 'ARB — Behind in Payment',
    requiresSubscription: true,
    templates: [
      {
        label: 'Update CC Info (Active/Suspended)',
        subject: 'Action Required: Update Your Payment Information',
        body:
          'One or more of your automatic payments for !JOBNAME for !PERSON was declined.\n\n' +
          'You can contact your credit card issuer to determine the reason if you need to.\n\n' +
          'Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.\n\n' +
          'Please !JOBLINK then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Update CC Info (will also pay for failed auto-payments)\'\n' +
          '4. Enter your credit card information and you will see the amount due at the bottom of the screen.\n' +
          '5. Click Submit to make the payment and reactivate your future automatic payments.'
      },
      {
        label: 'Pay Balance Due (Expired/Terminated)',
        subject: 'Action Required: Pay Balance Due',
        body:
          'One or more of your automatic payments for !JOBNAME for !PERSON was declined.\n\n' +
          'You can contact your credit card issuer to determine the reason if you need to.\n\n' +
          'Then you can update your credit card information and process the current balance due (!AMTOWED) all in one step.\n\n' +
          'Please !JOBLINK then:\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Pay Balance Due\''
      }
    ]
  },
  {
    category: 'ARB — Expiring Card',
    requiresSubscription: true,
    templates: [
      {
        label: 'Credit Card Expiration Notice',
        subject: 'Credit Card Expiring This Month',
        body:
          'Credit Card Expiration Notice\n\n' +
          'The credit card on file for Automatic Recurring Billing for !PERSON is expiring this month.\n\n' +
          'Please !JOBLINK to update your credit card information TO PREVENT YOUR NEXT PAYMENT FROM FAILING.\n\n' +
          '1. Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME\n' +
          '2. Select your Player\'s role\n' +
          '3. Under \'Player\' in the upper right, select \'Update CC Info (will also pay for failed auto-payments)\'\n' +
          '4. Enter your credit card information and you will see the amount due at the bottom of the screen\n' +
          '5. Click Submit to make the payment and reactivate your future automatic payments'
      }
    ]
  }
];
