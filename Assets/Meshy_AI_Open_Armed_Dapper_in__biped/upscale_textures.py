import os
from PIL import Image

# Path definitions
texture_dir = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped"
albedo_path = os.path.join(texture_dir, "Meshy_AI_Open_Armed_Dapper_in__biped_texture_0.png")
normal_path = os.path.join(texture_dir, "Meshy_AI_Open_Armed_Dapper_in__biped_texture_0_normal.png")

target_res = 8192 # 8K resolution

def upscale_image(image_path, target_size):
    if not os.path.exists(image_path):
        print(f"File not found: {image_path}")
        return
        
    print(f"Opening {os.path.basename(image_path)}...")
    img = Image.open(image_path)
    
    if img.size == (target_size, target_size):
        print(f"{os.path.basename(image_path)} is already at {target_size}x{target_size}.")
        return
        
    print(f"Upscaling from {img.size[0]}x{img.size[1]} to {target_size}x{target_size} using Lanczos resampling...")
    # Use Resampling.LANCZOS if available, fallback to ANTIALIAS for older PIL versions
    try:
        resample_filter = Image.Resampling.LANCZOS
    except AttributeError:
        resample_filter = Image.ANTIALIAS
        
    upscaled = img.resize((target_size, target_size), resample=resample_filter)
    
    print(f"Saving upscaled image to {image_path}...")
    upscaled.save(image_path, format="PNG")
    print(f"Successfully upscaled {os.path.basename(image_path)} to 8K!")

# Run upscaling
upscale_image(albedo_path, target_res)
upscale_image(normal_path, target_res)
print("Upscaling process complete!")
