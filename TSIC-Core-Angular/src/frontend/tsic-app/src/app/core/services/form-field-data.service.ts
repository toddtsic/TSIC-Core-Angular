import { Injectable } from '@angular/core';

export interface SelectOption {
    value: string;
    label: string;
}

/**
 * Provides sample/mock data for SELECT field dropdowns
 * This will be replaced with actual API calls once integrated
 */
@Injectable({
    providedIn: 'root'
})
export class FormFieldDataService {

    // Sample data for common dropdown fields
    private readonly dataSourceMappings: Record<string, SelectOption[]> = {
        genders: [
            { value: 'M', label: 'Male' },
            { value: 'F', label: 'Female' },
            { value: 'O', label: 'Other' }
        ],
        positions: [
            { value: 'attack', label: 'Attack' },
            { value: 'midfield', label: 'Midfield' },
            { value: 'defense', label: 'Defense' },
            { value: 'goalie', label: 'Goalie' },
            { value: 'lsm', label: 'LSM (Long Stick Middie)' },
            { value: 'fogo', label: 'FOGO (Face-Off Get-Off)' }
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
        skillLevels: [
            { value: 'beginner', label: 'Beginner' },
            { value: 'intermediate', label: 'Intermediate' },
            { value: 'advanced', label: 'Advanced' },
            { value: 'elite', label: 'Elite' }
        ],
        states: [
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
            { value: 'WY', label: 'Wyoming' }
        ],
        teams: [
            { value: '1', label: 'Sample Team A' },
            { value: '2', label: 'Sample Team B' },
            { value: '3', label: 'Sample Team C' }
        ],
        agegroups: [
            { value: 'u10', label: 'Under 10' },
            { value: 'u12', label: 'Under 12' },
            { value: 'u14', label: 'Under 14' },
            { value: 'u16', label: 'Under 16' },
            { value: 'u18', label: 'Under 18' }
        ],
        jerseySizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Youth XL', 'Adult S', 'Adult M', 'Adult L', 'Adult XL', 'Adult 2XL', 'Adult 3XL']),
        shortsSizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Youth XL', 'Adult S', 'Adult M', 'Adult L', 'Adult XL', 'Adult 2XL']),
        shirtSizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Youth XL', 'Adult S', 'Adult M', 'Adult L', 'Adult XL', 'Adult 2XL', 'Adult 3XL']),
        reversibleSizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Youth XL', 'Adult S', 'Adult M', 'Adult L', 'Adult XL']),
        kiltSizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Adult S', 'Adult M', 'Adult L']),
        sweatshirtSizes: this.generateSizes(['Youth S', 'Youth M', 'Youth L', 'Youth XL', 'Adult S', 'Adult M', 'Adult L', 'Adult XL', 'Adult 2XL', 'Adult 3XL']),
        sizes: this.generateSizes(['S', 'M', 'L', 'XL', '2XL', '3XL']),
        handedness: [
            { value: 'right', label: 'Right' },
            { value: 'left', label: 'Left' },
            { value: 'both', label: 'Both' }
        ]
    };

    /**
     * Get dropdown options for a given dataSource
     */
    getOptionsForDataSource(dataSource: string): SelectOption[] {
        return this.dataSourceMappings[dataSource] || [];
    }

    /**
     * Generate graduation year options (current year + 10 years)
     */
    private generateGradYears(): SelectOption[] {
        const currentYear = new Date().getFullYear();
        const years: SelectOption[] = [];

        for (let i = 0; i <= 10; i++) {
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
