local DRAWABLES = { masks = 1, hair = 2, torsos = 3, pants = 4, bags = 5, shoes = 6, necks = 7, undershirts = 8, vests = 9, decals = 10, jackets = 11 }
local PROPS = { hats = 0, glasses = 1, earrings = 2, watches = 6, bracelets = 7 }
local MODELS = { male = GetHashKey('mp_m_freemode_01'), female = GetHashKey('mp_f_freemode_01') }

local function starts(ped, counter, slot)
    local list, start = {}, 0
    for i = 0, GetPedCollectionsCount(ped) - 1 do
        local name = GetPedCollectionName(ped, i)
        local total = counter(ped, slot, name)
        if total > 0 then
            list[#list + 1] = { c = name, s = start }
            start = start + total
        end
    end
    return list
end

local function spawn(model)
    RequestModel(model)
    local timeout = GetGameTimer() + 10000
    while not HasModelLoaded(model) and GetGameTimer() < timeout do
        Wait(0)
    end
    local coords = GetEntityCoords(PlayerPedId())
    local ped = CreatePed(4, model, coords.x, coords.y, coords.z - 50.0, 0.0, false, true)
    SetModelAsNoLongerNeeded(model)
    return ped
end

RegisterNetEvent('clothes_refs:collect', function()
    local refs = {}
    for sex, model in pairs(MODELS) do
        local ped = spawn(model)
        Wait(500)
        local cats = {}
        for cat, slot in pairs(DRAWABLES) do
            cats[cat] = starts(ped, GetNumberOfPedCollectionDrawableVariations, slot)
        end
        for cat, slot in pairs(PROPS) do
            cats[cat] = starts(ped, GetNumberOfPedCollectionPropDrawableVariations, slot)
        end
        refs[sex] = cats
        DeleteEntity(ped)
    end
    TriggerLatentServerEvent('clothes_refs:result', 100000, json.encode(refs))
end)

RegisterNetEvent('clothes_refs:done', function(path)
    BeginTextCommandThefeedPost('STRING')
    AddTextComponentSubstringPlayerName('refs.json : ' .. path)
    EndTextCommandThefeedPostTicker(false, false)
end)
