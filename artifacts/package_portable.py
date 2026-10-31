import os
import shutil
import zipfile

def package_portable():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    source_dir = os.path.join(script_dir, "AirFlick-Updated")
    dest_dir = os.path.join(script_dir, "AirFlick-Portable-Previous")
    zip_path = os.path.join(script_dir, "AirFlick-Previous-Portable.zip")
    repo_root = os.path.abspath(os.path.join(script_dir, ".."))

    print(f"Source directory: {source_dir}")
    print(f"Destination directory: {dest_dir}")
    print(f"Zip destination: {zip_path}")

    if not os.path.exists(source_dir):
        raise FileNotFoundError(f"Source build directory not found: {source_dir}")

    # Copy tree
    print("Copying build files...")
    shutil.copytree(source_dir, dest_dir, dirs_exist_ok=True)

    # Ensure gesture files and models are included
    gesture_dest = os.path.join(dest_dir, "gesture")
    models_dest = os.path.join(gesture_dest, "models")
    os.makedirs(models_dest, exist_ok=True)

    gesture_src = os.path.join(repo_root, "gesture")
    engine_src = os.path.join(gesture_src, "gesture_engine.py")
    if os.path.exists(engine_src):
        shutil.copy2(engine_src, os.path.join(gesture_dest, "gesture_engine.py"))

    model_src = os.path.join(gesture_src, "models", "hand_landmarker.task")
    if os.path.exists(model_src):
        print(f"Bundling MediaPipe model: {model_src}")
        shutil.copy2(model_src, os.path.join(models_dest, "hand_landmarker.task"))

    # Verify essential files
    essential = [
        os.path.join(dest_dir, "AirFlick.exe"),
        os.path.join(dest_dir, "AirFlick.dll"),
        os.path.join(dest_dir, "AirFlick.deps.json"),
        os.path.join(gesture_dest, "main_gesture_service.py"),
    ]
    for path in essential:
        if not os.path.exists(path):
            print(f"WARNING: Missing expected file {path}")
        else:
            print(f"Verified: {os.path.basename(path)}")

    # Create zip archive
    if os.path.exists(zip_path):
        os.remove(zip_path)

    print(f"Creating zip file: {zip_path} ...")
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(dest_dir):
            for file in files:
                full_path = os.path.join(root, file)
                rel_path = os.path.relpath(full_path, dest_dir)
                zipf.write(full_path, rel_path)

    print(f"Archive created successfully: {os.path.getsize(zip_path)} bytes")

if __name__ == "__main__":
    package_portable()

