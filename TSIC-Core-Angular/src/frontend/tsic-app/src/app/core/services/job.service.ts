import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export interface JobBulletin {
    id: string;
    title: string;
    content: string;
    postedAt: string; // ISO date
}

export interface Job {
    jobPath: string;
    jobName: string;
    jobLogoPath: string; // URL or relative path
    jobBannerPath: string; // URL or relative path
    jobBulletins: JobBulletin[];
}

@Injectable({ providedIn: 'root' })
export class JobService {
    private readonly currentJobSubject = new BehaviorSubject<Job | null>(null);
    public readonly currentJob$: Observable<Job | null> = this.currentJobSubject.asObservable();


    // Simulate fetching job info (replace with real API call as needed)
    setJob(job: Job) {
        this.currentJobSubject.next(job);
    }

    // Example: fetch job details from API (to be implemented)
    // fetchJob(jobPath: string): Observable<Job> {
    //   return this.http.get<Job>(`/api/jobs/${jobPath}`);
    // }

    getCurrentJob(): Job | null {
        return this.currentJobSubject.value;
    }
}
