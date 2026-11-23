import os
import sys
import json
from pathlib import Path

MEMORY_FILE = Path(__file__).parent / "canonical_memories.json"
def load_dotenv():
    search_dirs = [
        Path.cwd(),
        Path(__file__).resolve().parent.parent.parent,
        Path(__file__).resolve().parent,
    ]
    for d in search_dirs:
        env_path = d / ".env"
        if env_path.exists():
            with open(env_path, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line and not line.startswith("#") and "=" in line:
                        k, v = line.split("=", 1)
                        os.environ.setdefault(k.strip(), v.strip().strip("'\""))
            break

load_dotenv()
COLLECTION_NAME = os.getenv("MILVUS_COLLECTION", "focusfence_canonical_memories")
HOST = os.getenv("MILVUS_HOST", "")
PORT = os.getenv("MILVUS_PORT", "19530")
USER = os.getenv("MILVUS_USER", "")
PASSWORD = os.getenv("MILVUS_PASSWORD", "")
TOKEN = os.getenv("MILVUS_TOKEN", "")

def load_memories():
    if not MEMORY_FILE.exists():
        print(f"Error: {MEMORY_FILE} not found.")
        return []
    with open(MEMORY_FILE, "r", encoding="utf-8") as f:
        return json.load(f)

def status():
    memories = load_memories()
    print(f"Local canonical memories: {len(memories)} entries loaded.")
    for m in memories:
        print(f" - [{m['id']}] {m['title']} ({len(m.get('tags', []))} tags)")
    if not HOST:
        print("\nContabo Milvus Status: UNCONFIGURED")
        print("To connect, set environment variables:")
        print("  $env:MILVUS_HOST = \"<contabo-server-ip-or-host>\"")
        print("  $env:MILVUS_PORT = \"19530\"")
        print("  $env:MILVUS_USER = \"<username>\"")
        print("  $env:MILVUS_PASSWORD = \"<password>\"")
        return
    print(f"\nContabo Milvus Host: {HOST}:{PORT}")

def sync_all():
    memories = load_memories()
    print(f"Synchronizing {len(memories)} canonical memories to Contabo Milvus...")
    if not HOST:
        print("Error: MILVUS_HOST environment variable is not set.")
        print("Please configure MILVUS_HOST (and optional MILVUS_PORT, MILVUS_USER, MILVUS_PASSWORD).")
        sys.exit(1)
    try:
        from pymilvus import connections, utility, Collection, FieldSchema, CollectionSchema, DataType
    except ImportError:
        print("Error: 'pymilvus' is not installed.")
        print("Install it using: pip install pymilvus")
        sys.exit(1)

    connect_kwargs = {"alias": "default", "host": HOST, "port": PORT}
    if USER and PASSWORD:
        connect_kwargs["user"] = USER
        connect_kwargs["password"] = PASSWORD
    elif TOKEN:
        connect_kwargs["token"] = TOKEN

    print(f"Connecting to Milvus at {HOST}:{PORT}...")
    connections.connect(**connect_kwargs)
    print("Connected successfully.")

    if not utility.has_collection(COLLECTION_NAME):
        print(f"Creating collection '{COLLECTION_NAME}'...")
        fields = [
            FieldSchema(name="id", dtype=DataType.VARCHAR, is_primary=True, max_length=128),
            FieldSchema(name="domain", dtype=DataType.VARCHAR, max_length=128),
            FieldSchema(name="category", dtype=DataType.VARCHAR, max_length=128),
            FieldSchema(name="title", dtype=DataType.VARCHAR, max_length=256),
            FieldSchema(name="summary", dtype=DataType.VARCHAR, max_length=4096),
            FieldSchema(name="tags", dtype=DataType.VARCHAR, max_length=512)
        ]
        schema = CollectionSchema(fields, description="FocusFence Canonical Memories")
        collection = Collection(name=COLLECTION_NAME, schema=schema)
    else:
        collection = Collection(name=COLLECTION_NAME)

    entities = [
        [m["id"] for m in memories],
        [m["domain"] for m in memories],
        [m["category"] for m in memories],
        [m["title"] for m in memories],
        [m["summary"] for m in memories],
        [",".join(m.get("tags", [])) for m in memories]
    ]
    collection.insert(entities)
    collection.flush()
    print(f"Successfully synchronized {len(memories)} entries into collection '{COLLECTION_NAME}'.")

if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else "status"
    if action == "sync-all":
        sync_all()
    else:
        status()
