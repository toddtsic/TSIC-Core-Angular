export default {
    input: 'http://localhost:5022/swagger/v1/swagger.json',
    output: './src/app/core/api/models',
    httpClient: 'fetch',
    useOptions: false,
    useUnionTypes: false,
    exportCore: false,
    exportServices: false,
    exportModels: true,
    exportSchemas: false,
};
