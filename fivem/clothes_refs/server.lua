local pending = {}

RegisterCommand('clothes_refs', function(source)
    if source == 0 then
        print('[clothes_refs] run this command in game.')
        return
    end
    pending[source] = true
    TriggerClientEvent('clothes_refs:collect', source)
end, true)

RegisterNetEvent('clothes_refs:result', function(data)
    local src = source
    if not pending[src] then return end
    pending[src] = nil
    local refs = type(data) == 'string' and json.decode(data)
    if type(refs) ~= 'table' or type(refs.male) ~= 'table' or type(refs.female) ~= 'table' then
        print('[clothes_refs] invalid data from ' .. GetPlayerName(src))
        return
    end
    local resource = GetCurrentResourceName()
    SaveResourceFile(resource, 'refs.json', json.encode(refs), -1)
    local path = GetResourcePath(resource) .. '/refs.json'
    print('[clothes_refs] written ' .. path)
    TriggerClientEvent('clothes_refs:done', src, path)
end)

AddEventHandler('playerDropped', function()
    pending[source] = nil
end)
