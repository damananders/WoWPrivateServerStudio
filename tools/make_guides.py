"""Builds the pictures for the in-app "How to use this" guides from a screenshot run.

    AndersCraftStudio.exe --screens <dir> --public        (redacted screenshots, read-only)
    python tools/make_guides.py <dir>

Writes src/App/Guides/<key>.jpg (1600x1000). The numbered markers are drawn by the app (GuideWindow), not baked in.
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

# Text boxes are not redacted by --public (changing them would fire edit handlers), so neutral text is painted over
# them here: (left, top, right, bottom) of the box interior at 1600x1000, then the replacement text.
REDACT = {
    'wizard': [((424, 357, 888, 396), 'studio_ro'), ((972, 376, 1440, 415), 'studio_rw')],
    'patches': [((330, 345, 1410, 396), r'C:\Games\WoW 3.3.5a'),
                ((330, 645, 1299, 696), r'C:\Users\you\Documents\AndersCraft Studio\patch_work\patch-W')],
}

PICTURES = {
    'wizard': '03_wizard_accounts.png',
    'creatures': '21_creatures_basics.png',
    'items': '28_items_basics.png',
    'quests': '32_quests_basics.png',
    'loot': '50_loot_creature.png',
    'loot_sim': '51_loot_simulator.png',
    'smartai': '55_smartai_builder.png',
    'spells': '127_spells_spellbook_check.png',
    'spells_create': '126_spells_create_guided.png',
    'spells_problems': '128_spells_spellbook_problems.png',
    'spells_send': '129_spells_send_to_game.png',
    'dbc': '65_dbc_unstaged_edit.png',
    'patches': '40b_client_patches_built.png',
    'assets': '88_assets_icon_imported.png',
    'sql': '08_sql_browser_staged_edit.png',
    'admin': '106_admin_login.png',
    'changes': '16_changes_apply_panel.png',
    'server_dash': '140_server_dashboard.png',
    'qb_start': '155_quest_builder_from_game.png',
    'qb_objectives': '152_quest_builder_objectives.png',
    'qb_review': '154_quest_builder_review.png',
    'nb_look': '158_npc_builder_look.png',
    'nb_where': '160_npc_builder_where.png',
    'bb_pick': '162_behaviour_builder_pick.png',
    'bb_details': '163_behaviour_builder_details.png',
    'server_players': '141_server_players.png',
    'server_player': '142_server_player_window.png',
    'server_console': '144_server_console.png',
    'server_schedule': '146_server_schedule.png',
    'changes_dbc': '124_changes_dbc_sent_status.png',
    'changes_history': '17_history_rollback.png',
}

def main(src):
    out = os.path.join(os.path.dirname(__file__), '..', 'src', 'App', 'Guides')
    os.makedirs(out, exist_ok=True)
    for key, name in PICTURES.items():
        p = os.path.join(src, name)
        if not os.path.exists(p):
            print('missing', name); continue
        im = Image.open(p).convert('RGB')
        if abs(im.width / im.height - 1.6) > 0.02:
            # a window picture: fit it inside 1600x1000 on its own background instead of stretching it
            sc = min(1600 / im.width, 1000 / im.height)
            small = im.resize((round(im.width * sc), round(im.height * sc)), Image.LANCZOS)
            canvas = Image.new('RGB', (1600, 1000), im.getpixel((6, 6)))
            canvas.paste(small, ((1600 - small.width) // 2, (1000 - small.height) // 2))
            im = canvas
        else:
            im = im.resize((1600, 1000), Image.LANCZOS)
        if key in REDACT:
            d = ImageDraw.Draw(im)
            font = ImageFont.truetype(r'C:\Windows\Fonts\consola.ttf', 16)
            for (l, t, r, b), text in REDACT[key]:
                bg = im.getpixel((l + 3, t + 3))
                d.rectangle((l, t, r, b), fill=bg)
                d.text((l + 12, (t + b) // 2), text, fill=(236, 230, 222), font=font, anchor='lm')
        im.save(os.path.join(out, key + '.jpg'), quality=84, optimize=True, progressive=True)
        print(key, os.path.getsize(os.path.join(out, key + '.jpg')) // 1024, 'KB')

if __name__ == '__main__':
    main(sys.argv[1])
