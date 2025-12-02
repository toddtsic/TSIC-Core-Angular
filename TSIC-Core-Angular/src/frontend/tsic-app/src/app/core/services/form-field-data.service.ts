import { Injectable, signal } from '@angular/core';

export interface SelectOption {
    value: string;
    label: string;
}

/**
 * Provides dropdown options for SELECT fields
 * Combines static fallback data with dynamic job-specific options from Jobs.JsonOptions
 */
@Injectable({
    providedIn: 'root'
})
export class FormFieldDataService {

    // Job-specific options loaded dynamically (from Jobs.JsonOptions)
    private readonly _jobOptions = signal<Record<string, SelectOption[]> | null>(null);

    /**
     * Load job-specific options from Jobs.JsonOptions
     * @param jsonOptionsString - The JSON string from Job.JsonOptions property
     */
    setJobOptions(jsonOptionsString: string | null | undefined): void {
        if (!jsonOptionsString) {
            this._jobOptions.set(null);
            return;
        }

        try {
            const parsed = JSON.parse(jsonOptionsString);
            const normalized: Record<string, SelectOption[]> = {};

            for (const [key, value] of Object.entries(parsed)) {
                if (Array.isArray(value)) {
                    // Parse format: [{"Text":"Attack","Value":"attack"}, ...]
                    normalized[key] = value.map((item: any) => ({
                        value: item.Value ?? item.value ?? '',
                        label: item.Text ?? item.text ?? item.Label ?? item.label ?? ''
                    }));
                }
            }

            this._jobOptions.set(normalized);
        } catch (error) {
            console.error('Failed to parse job JsonOptions:', error);
            this._jobOptions.set(null);
        }
    }

    // Static fallback data for universal dropdown fields
    // Based on CSharpToMetadataParser.InferDataSource() - fields expected to be consistent across all jobs
    private readonly fallbackMappings: Record<string, SelectOption[]> = {
        // Universal fields (same values across all jobs/sports)
        genders: [
            { value: 'M', label: 'Male' },
            { value: 'F', label: 'Female' }
        ],
        gradYears: this.generateGradYears(),
        schoolGrades: [
            { value: '6', label: '6th Grade' },
            { value: '7', label: '7th Grade' },
            { value: '8', label: '8th Grade' },
            { value: '9', label: '9th Grade (Freshman)' },
            { value: '10', label: '10th Grade (Sophomore)' },
            { value: '11', label: '11th Grade (Junior)' },
            { value: '12', label: '12th Grade (Senior)' }
        ],
        states: [
            // US States
            { value: 'AL', label: 'Alabama' },
            { value: 'AK', label: 'Alaska' },
            { value: 'AZ', label: 'Arizona' },
            { value: 'AR', label: 'Arkansas' },
            { value: 'CA', label: 'California' },
            { value: 'CO', label: 'Colorado' },
            { value: 'CT', label: 'Connecticut' },
            { value: 'DE', label: 'Delaware' },
            { value: 'FL', label: 'Florida' },
            { value: 'GA', label: 'Georgia' },
            { value: 'HI', label: 'Hawaii' },
            { value: 'ID', label: 'Idaho' },
            { value: 'IL', label: 'Illinois' },
            { value: 'IN', label: 'Indiana' },
            { value: 'IA', label: 'Iowa' },
            { value: 'KS', label: 'Kansas' },
            { value: 'KY', label: 'Kentucky' },
            { value: 'LA', label: 'Louisiana' },
            { value: 'ME', label: 'Maine' },
            { value: 'MD', label: 'Maryland' },
            { value: 'MA', label: 'Massachusetts' },
            { value: 'MI', label: 'Michigan' },
            { value: 'MN', label: 'Minnesota' },
            { value: 'MS', label: 'Mississippi' },
            { value: 'MO', label: 'Missouri' },
            { value: 'MT', label: 'Montana' },
            { value: 'NE', label: 'Nebraska' },
            { value: 'NV', label: 'Nevada' },
            { value: 'NH', label: 'New Hampshire' },
            { value: 'NJ', label: 'New Jersey' },
            { value: 'NM', label: 'New Mexico' },
            { value: 'NY', label: 'New York' },
            { value: 'NC', label: 'North Carolina' },
            { value: 'ND', label: 'North Dakota' },
            { value: 'OH', label: 'Ohio' },
            { value: 'OK', label: 'Oklahoma' },
            { value: 'OR', label: 'Oregon' },
            { value: 'PA', label: 'Pennsylvania' },
            { value: 'RI', label: 'Rhode Island' },
            { value: 'SC', label: 'South Carolina' },
            { value: 'SD', label: 'South Dakota' },
            { value: 'TN', label: 'Tennessee' },
            { value: 'TX', label: 'Texas' },
            { value: 'UT', label: 'Utah' },
            { value: 'VT', label: 'Vermont' },
            { value: 'VA', label: 'Virginia' },
            { value: 'WA', label: 'Washington' },
            { value: 'WV', label: 'West Virginia' },
            { value: 'WI', label: 'Wisconsin' },
            { value: 'WY', label: 'Wyoming' },
            // Canadian Provinces
            { value: 'AB', label: 'Alberta' },
            { value: 'BC', label: 'British Columbia' },
            { value: 'MB', label: 'Manitoba' },
            { value: 'NB', label: 'New Brunswick' },
            { value: 'NL', label: 'Newfoundland and Labrador' },
            { value: 'NT', label: 'Northwest Territories' },
            { value: 'NS', label: 'Nova Scotia' },
            { value: 'NU', label: 'Nunavut' },
            { value: 'ON', label: 'Ontario' },
            { value: 'PE', label: 'Prince Edward Island' },
            { value: 'QC', label: 'Quebec' },
            { value: 'SK', label: 'Saskatchewan' },
            { value: 'YT', label: 'Yukon' }
        ],
        handedness: [
            { value: 'right', label: 'Right' },
            { value: 'left', label: 'Left' },
            { value: 'both', label: 'Both' }
        ]
        // NOTE: The following MUST come from job-specific JsonOptions per CSharpToMetadataParser.InferDataSource():
        // Job/Sport-specific (defined in JsonOptions only):
        // - List_Positions (varies by sport: lacrosse attack/midfield vs soccer forward/striker)
        // - List_SkillLevels (potentially event-specific)
        // - List_YearsExperience (potentially job-specific)
        // - List_WhoReferred (job-specific referral sources)
        // - List_HeightInches (potentially job-specific ranges)
        // - List_RecruitingGradYears (potentially different from standard gradYears)
        // Size variants (vendor/product-specific):
        // - ListSizes_Jersey, ListSizes_Shorts, ListSizes_Tshirt, ListSizes_Sweatshirt
        // - ListSizes_Kilt, ListSizes_Reversible, ListSizes_Gloves, ListSizes_Shoes
        // - Any other *Size fields (dynamic: ListSizes_{PropertyName})
        // Event/Job-specific:
        // - teams (job-specific team names)
        // - agegroups (event-specific age divisions)
        // - levelOfPlay, divisions (event structure)
    };

    /**
     * Get dropdown options for a given dataSource
     * Searches job-specific options first, then falls back to static data
     */
    getOptionsForDataSource(dataSource: string): SelectOption[] {
        // Priority 1: Try job-specific options with fuzzy matching
        const jobOpts = this._jobOptions();
        if (jobOpts) {
            const match = this.findJobOptionsKey(jobOpts, dataSource);
            if (match && jobOpts[match]) {
                return jobOpts[match];
            }
        }

        // Priority 2: Fall back to static data
        return this.fallbackMappings[dataSource] || [];
    }

    /**
     * Find matching key in job options using fuzzy matching
     * Examples: "positions" -> "List_Positions", "jerseySize" -> "ListSizes_Jersey"
     */
    private findJobOptionsKey(jobOptions: Record<string, SelectOption[]>, dataSource: string): string | null {
        const normalize = (s: string) => s.toLowerCase().replaceAll(/[^a-z0-9]/g, '');
        const dsNorm = normalize(dataSource);

        // Exact match
        for (const key of Object.keys(jobOptions)) {
            if (normalize(key) === dsNorm) return key;
        }

        // Contains match
        for (const key of Object.keys(jobOptions)) {
            const keyNorm = normalize(key);
            if (keyNorm.includes(dsNorm) || dsNorm.includes(keyNorm)) {
                return key;
            }
        }

        // Prefix variants: try "List_", "ListSizes_"
        const withList = `list${dsNorm}`;
        const withListSizes = `listsizes${dsNorm}`;

        for (const key of Object.keys(jobOptions)) {
            const keyNorm = normalize(key);
            if (keyNorm === withList || keyNorm === withListSizes) {
                return key;
            }
        }

        return null;
    }

    /**
     * Generate graduation year options (current year forward 12 years)
     * For registering kids - youngest graduates this year, oldest graduates 12 years from now
     */
    private generateGradYears(): SelectOption[] {
        const currentYear = new Date().getFullYear();
        const years: SelectOption[] = [];

        // Add special case for already graduated
        years.push({ value: 'graduated', label: 'Already Graduated' });

        // Add special case for preschool age children
        years.push({ value: 'preschool', label: 'Preschool' });

        // Add year options from current year forward
        for (let i = 0; i <= 12; i++) {
            const year = currentYear + i;
            years.push({ value: year.toString(), label: year.toString() });
        }

        return years;
    }

    /**
     * Generate size options from array of size strings
     */
    private generateSizes(sizes: string[]): SelectOption[] {
        return sizes.map(size => ({ value: size, label: size }));
    }
}
