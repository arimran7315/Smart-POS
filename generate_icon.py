import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter

def create_smartpos_icon():
    # Render at 1024x1024 for supersampling
    size = 1024
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. Background Squircle / Rounded Rectangle
    margin = 48
    radius = 200
    rect = [margin, margin, size - margin, size - margin]

    # Create gradient background
    grad = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad_draw = ImageDraw.Draw(grad)
    for y in range(margin, size - margin):
        ratio = (y - margin) / (size - 2 * margin)
        # Deep Indigo (#1E3A8A: 30, 58, 138) to Emerald/Teal (#0F766E: 15, 118, 110)
        r = int(24 + ratio * (15 - 24))
        g = int(48 + ratio * (130 - 48))
        b = int(140 + ratio * (120 - 140))
        grad_draw.line([(margin, y), (size - margin, y)], fill=(r, g, b, 255))

    # Mask for squircle
    mask = Image.new("L", (size, size), 0)
    mask_draw = ImageDraw.Draw(mask)
    mask_draw.rounded_rectangle(rect, radius=radius, fill=255)

    # Composite gradient with mask
    base = Image.composite(grad, img, mask)
    draw = ImageDraw.Draw(base)

    # Soft inner border / highlight
    border_draw = ImageDraw.Draw(base)
    border_draw.rounded_rectangle(rect, radius=radius, outline=(255, 255, 255, 45), width=12)

    # Subtle top highlight
    highlight = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    h_draw = ImageDraw.Draw(highlight)
    h_draw.rounded_rectangle([margin+10, margin+10, size-margin-10, size//2], radius=radius-10, fill=(255, 255, 255, 20))
    h_mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(h_mask).rounded_rectangle(rect, radius=radius, fill=255)
    base = Image.alpha_composite(base, highlight)

    draw = ImageDraw.Draw(base)

    # 2. Modern POS Terminal / Register Screen & Card / Scanner graphics
    # POS Base / Stand
    stand_color = (200, 220, 240, 230)
    draw.polygon([(440, 720), (584, 720), (620, 820), (404, 820)], fill=(30, 41, 59, 200))
    draw.rounded_rectangle([360, 810, 664, 840], radius=15, fill=(148, 163, 184, 255))

    # POS Screen Frame (Tablet / Terminal screen)
    screen_rect = [230, 230, 794, 730]
    draw.rounded_rectangle(screen_rect, radius=48, fill=(15, 23, 42, 255), outline=(59, 130, 246, 255), width=10)

    # Display Area
    display_rect = [260, 260, 764, 690]
    draw.rounded_rectangle(display_rect, radius=32, fill=(30, 41, 59, 255))

    # Header Bar on screen
    draw.rounded_rectangle([260, 260, 764, 340], radius=24, fill=(37, 99, 235, 255))
    # Small circles for status
    draw.ellipse([290, 288, 314, 312], fill=(52, 211, 153, 255)) # Green status
    draw.rounded_rectangle([330, 292, 450, 308], radius=8, fill=(255, 255, 255, 180)) # Search / App bar

    # POS Cart / Receipt / Barcode Visuals on Screen
    # Left: Cart items / Barcode
    # Barcode lines
    bc_x = 300
    bc_y = 370
    bc_h = 100
    widths = [8, 16, 8, 24, 12, 8, 20, 8, 16, 12, 8, 20, 12, 8, 16]
    for w in widths:
        draw.rectangle([bc_x, bc_y, bc_x + w, bc_y + bc_h], fill=(56, 189, 248, 240))
        bc_x += w + 8

    # Scanner Laser Line across barcode (Bright Red / Orange / Cyan Glow)
    draw.line([(280, 420), (590, 420)], fill=(239, 68, 68, 255), width=8)

    # Right / Middle: Modern Check / Payment / Currency badge
    # Receipt Card / Total Card
    draw.rounded_rectangle([300, 500, 724, 650], radius=20, fill=(51, 65, 85, 255), outline=(71, 85, 105, 255), width=4)
    
    # Text lines on checkout card
    draw.rounded_rectangle([330, 530, 480, 546], radius=8, fill=(148, 163, 184, 200))
    draw.rounded_rectangle([330, 560, 420, 574], radius=7, fill=(100, 116, 139, 200))
    draw.rounded_rectangle([330, 595, 520, 620], radius=10, fill=(34, 197, 94, 255)) # Green total pill

    # Shopping / Card floating badge (Top Right overlay)
    badge_rect = [600, 160, 850, 410]
    draw.rounded_rectangle(badge_rect, radius=40, fill=(16, 185, 129, 255), outline=(255, 255, 255, 220), width=10)

    # Draw Shopping Cart / Checkmark in badge
    # Shopping Cart vector
    # Cart body
    draw.line([(650, 240), (680, 240), (710, 320), (780, 320), (800, 260), (670, 260)], fill=(255, 255, 255, 255), width=10)
    # Cart wheels
    draw.ellipse([705, 335, 725, 355], fill=(255, 255, 255, 255))
    draw.ellipse([765, 335, 785, 355], fill=(255, 255, 255, 255))

    # Fast downscale with LANCZOS for crystal sharp icon
    final_256 = base.resize((256, 256), Image.Resampling.LANCZOS)
    final_128 = base.resize((128, 128), Image.Resampling.LANCZOS)
    final_64 = base.resize((64, 64), Image.Resampling.LANCZOS)
    final_48 = base.resize((48, 48), Image.Resampling.LANCZOS)
    final_32 = base.resize((32, 32), Image.Resampling.LANCZOS)
    final_16 = base.resize((16, 16), Image.Resampling.LANCZOS)

    return base, [final_256, final_128, final_64, final_48, final_32, final_16]

if __name__ == "__main__":
    os.makedirs(r"D:\POS\src\SmartPOS.App\Assets", exist_ok=True)
    os.makedirs(r"D:\POS\packaging\SmartPOS\Assets", exist_ok=True)
    
    hi_res, icon_sizes = create_smartpos_icon()

    # Save PNGs
    hi_res.save(r"D:\POS\src\SmartPOS.App\Assets\SmartPOS.png", format="PNG")
    hi_res.save(r"D:\POS\packaging\SmartPOS\Assets\SmartPOS.png", format="PNG")

    # Save ICO containing all standard Windows sizes
    ico_path_app = r"D:\POS\src\SmartPOS.App\SmartPOS.ico"
    ico_path_pkg = r"D:\POS\packaging\SmartPOS\SmartPOS.ico"
    
    icon_sizes[0].save(
        ico_path_app,
        format="ICO",
        sizes=[(s.width, s.height) for s in icon_sizes],
        append_images=icon_sizes[1:]
    )
    icon_sizes[0].save(
        ico_path_pkg,
        format="ICO",
        sizes=[(s.width, s.height) for s in icon_sizes],
        append_images=icon_sizes[1:]
    )
    print("Icon generated successfully!")
