// Generic UI state wrappers used across the app

export type Loadable<T> = {
    loading: boolean;
    data: T | null;
    error: string | null;
};
