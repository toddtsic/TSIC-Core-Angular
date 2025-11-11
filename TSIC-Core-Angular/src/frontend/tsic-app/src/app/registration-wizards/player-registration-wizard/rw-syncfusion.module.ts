import { NgModule, CUSTOM_ELEMENTS_SCHEMA, NO_ERRORS_SCHEMA } from '@angular/core';
import { DropDownListModule, MultiSelectModule } from '@syncfusion/ej2-angular-dropdowns';

// Wrapper NgModule to encapsulate Syncfusion Angular modules so standalone
// components can import one stable module alongside other standalone imports
// without confusing the Angular compiler's static analyzer.
@NgModule({
    imports: [DropDownListModule, MultiSelectModule],
    exports: [DropDownListModule, MultiSelectModule],
    schemas: [CUSTOM_ELEMENTS_SCHEMA, NO_ERRORS_SCHEMA]
})
export class RwSyncfusionModule { }
