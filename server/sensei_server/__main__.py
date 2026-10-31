import os
import uvicorn

uvicorn.run("sensei_server.app:app", host="0.0.0.0", port=int(os.getenv("PORT", "8000")), log_level="warning", access_log=False)
