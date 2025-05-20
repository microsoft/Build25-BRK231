1. create external tenant
    1. create an extenal id tenant
2. App Registrations
    2. register an app for the Woodgrove Groceries API
        2. create client secret
    3. register an app for the Agent API
        2. create client secret
    4. register an app for the WebApp.
        2. create client secret
3. User Flows
    1. create extension fields
        2. date of birth
        3. dietary restrictions
        4. Name?? 
    1. register a SUSI flow for the Web App
        2. configure extension attributes for sign-up flow
4. Permissions
    1. expose apis / scopes in woodgrove groceries apis
    2. give permissions for the woodgrove apis to the Agent API
        3. Give admin consent to woodgrove groceries API in Agent App
        4. configure token to include extension attributes in woodgrove groceries API
    3. expose agent api permissions / scope in Agent AI
    4. configure Agent API to receive extension fields
    5. give permissions to web app to consume Agent API permissions
        6. give admin consent 
        7. configure web app to receive extension attributes
5. configure appsettings in APIs to reflect ClientId / TenantID 
