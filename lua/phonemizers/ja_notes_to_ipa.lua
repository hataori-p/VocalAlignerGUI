local profile = {}

profile.id           = "ja_notes_to_ipa"
profile.display_name = "Japanese Notes → Stable IPA"
profile.encoding     = "IPA"

-- Single Source of Truth for Vowels (Stable IPA)
local VOWEL_SET = { ["a"]=true, ["i"]=true, ["ɯ"]=true, ["e"]=true, ["o"]=true, ["ɴ"]=true }

-- Internal dictionaries
local _romaji_to_ipa = nil
local _kana_to_romaji = nil

local function ensure_data()
    if _romaji_to_ipa then return end
    -- Load existing mapping files
    _romaji_to_ipa = vocal.io.load_dictionary("lua/data/romaji_to_ipa.txt", "=")
    _kana_to_romaji = vocal.io.load_dictionary("lua/data/kana_map.txt", "=")
end

-- Helper: Clean pitch info like "ka(-6)" or "ka (C4)"
local function strip_metadata(text)
    return text:gsub("%s*%b()$", ""):gsub("%s+", "")
end

-- Universal Mapper: Kana/Romaji -> List of Stable IPA tokens
local function get_ipa_tokens(text)
    if text == "" or text == "_" or text == "sp" then return {"sil"} end
    
    -- 1. Try Kana lookup first 
    local kana_res = _kana_to_romaji.get(text)
    local target = kana_res or text
    
    -- 2. Greedy Tokenizer (Ported from ja_transcriber logic)
    -- This handles "kyu" -> {"ky", "u"} or "ka" -> {"k", "a"}
    local tokens = {}
    -- Logic simplified for this standalone script: 
    -- split space or greedy match if no spaces
    for word in target:gmatch("%S+") do
        -- Check if direct mapping exists (e.g. "ky", "sh", "ts")
        local ipa = _romaji_to_ipa.get(word)
        if ipa then 
            table.insert(tokens, ipa) 
        else
            -- Break down Romaji syllables like "ka" -> "k", "a"
            -- Using a simplified version of your greedy Romaji logic
            local i = 1
            while i <= #word do
                local found = false
                for len = 3, 1, -1 do
                    local sub = word:sub(i, i + len - 1)
                    local map = _romaji_to_ipa.get(sub)
                    if map then
                        table.insert(tokens, map)
                        i = i + len
                        found = true
                        break
                    end
                end
                if not found then i = i + 1 end
            end
        end
    end
    return tokens
end

function profile.g2p_batch(lines)
    ensure_data()
    
    local num_intervals = #lines
    local work_table = {}
    for i = 1, num_intervals do work_table[i] = {} end
    
    local last_vowel = "sil"

    for i, line in ipairs(lines) do
        local raw_text = vocal.text.trim(line)
        local clean_text = strip_metadata(raw_text)
        
        if clean_text == "-" then
            -- Vowel repetition
            table.insert(work_table[i], last_vowel)
        else
            local ipa_list = get_ipa_tokens(clean_text)
            
            -- Find first vowel to perform the shift
            local first_v_idx = -1
            for idx, ph in ipairs(ipa_list) do
                if VOWEL_SET[ph] then
                    first_v_idx = idx
                    break
                end
            end

            if first_v_idx > 0 then
                -- Shift initial consonants to PREVIOUS interval
                if i > 1 then
                    for c_idx = 1, first_v_idx - 1 do
                        table.insert(work_table[i-1], ipa_list[c_idx])
                    end
                end
                
                -- Keep nucleus and coda in CURRENT interval
                for v_idx = first_v_idx, #ipa_list do
                    local ph = ipa_list[v_idx]
                    table.insert(work_table[i], ph)
                    if VOWEL_SET[ph] then last_vowel = ph end
                end
            else
                -- No vowel (silence, 'br', or lone consonant)
                -- Keep everything in current interval
                for _, ph in ipairs(ipa_list) do
                    table.insert(work_table[i], ph)
                end
                if clean_text == "_" or clean_text == "sp" then last_vowel = "sil" end
            end
        end
    end

    -- Convert lists to space-separated strings for C#
    local final_results = {}
    for i = 1, num_intervals do
        if #work_table[i] == 0 then
            final_results[i] = "_" -- Keep it as a visual silence placeholder
        else
            final_results[i] = table.concat(work_table[i], " ")
        end
    end
    
    return final_results
end

return profile
