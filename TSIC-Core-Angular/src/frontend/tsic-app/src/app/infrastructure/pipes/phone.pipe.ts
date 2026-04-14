import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'phone', standalone: true })
export class PhonePipe implements PipeTransform {
    transform(value: string | null | undefined, fallback = '—'): string {
        if (!value) return fallback;
        let d = value.replace(/\D/g, '');
        if (d.length === 11 && d.startsWith('1')) d = d.slice(1);
        return d.length === 10
            ? `${d.slice(0, 3)}-${d.slice(3, 6)}-${d.slice(6)}`
            : value;
    }
}
