import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type {
    CustomerGroupDto,
    CustomerGroupMemberDto,
    CustomerLookupDto,
    CreateCustomerGroupRequest,
    RenameCustomerGroupRequest,
    AddCustomerGroupMemberRequest
} from '@core/api';

@Injectable({ providedIn: 'root' })
export class CustomerGroupsService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = `${environment.apiUrl}/customer-groups`;

    getGroups(): Observable<CustomerGroupDto[]> {
        return this.http.get<CustomerGroupDto[]>(this.apiUrl);
    }

    createGroup(request: CreateCustomerGroupRequest): Observable<CustomerGroupDto> {
        return this.http.post<CustomerGroupDto>(this.apiUrl, request);
    }

    renameGroup(groupId: number, request: RenameCustomerGroupRequest): Observable<CustomerGroupDto> {
        return this.http.put<CustomerGroupDto>(`${this.apiUrl}/${groupId}`, request);
    }

    deleteGroup(groupId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${groupId}`);
    }

    getMembers(groupId: number): Observable<CustomerGroupMemberDto[]> {
        return this.http.get<CustomerGroupMemberDto[]>(`${this.apiUrl}/${groupId}/members`);
    }

    addMember(groupId: number, request: AddCustomerGroupMemberRequest): Observable<CustomerGroupMemberDto> {
        return this.http.post<CustomerGroupMemberDto>(`${this.apiUrl}/${groupId}/members`, request);
    }

    removeMember(groupId: number, memberId: number): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${groupId}/members/${memberId}`);
    }

    getAvailableCustomers(groupId: number): Observable<CustomerLookupDto[]> {
        return this.http.get<CustomerLookupDto[]>(`${this.apiUrl}/${groupId}/available-customers`);
    }
}
