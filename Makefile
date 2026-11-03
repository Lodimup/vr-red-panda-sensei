# Red Panda Sensei WebXR app: build, run and publish on this machine.
#
#   make up VOICE_REF=/path/to/ref.wav   build, start the server in the background, publish over Tailscale HTTPS
#   make down                            stop the server and remove the Tailscale route
#
# The OpenRouter key comes from SENSEI_LLM_API_KEY; when it is unset, `make up` / `make run` ask for it
# (input hidden). Logic lives in tools/web/serve.sh.

PORT ?= 8000
HTTPS_PORT ?= 443
VOICE_REF ?=
export PORT HTTPS_PORT VOICE_REF

SERVE := tools/web/serve.sh

.DEFAULT_GOAL := help
.PHONY: help deps build run start tunnel url up untunnel stop down status logs

help: ## List targets
	@grep -E '^[a-z]+:.*## ' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  make %-9s %s\n", $$1, $$2}'

deps: ## Install client and server dependencies (needs node/npm and uv)
	@command -v npm >/dev/null || { echo "npm not found: install Node.js"; exit 1; }
	@command -v uv >/dev/null || { echo "uv not found: https://docs.astral.sh/uv/"; exit 1; }
	cd client && npm ci
	cd server && uv sync

build: deps ## Build the client into client/dist
	cd client && npm run build

run: build ## Run the server in the foreground
	@bash $(SERVE) run

start: build ## Start the server in the background and wait for /healthz
	@bash $(SERVE) start

tunnel: ## Publish the server on this machine's Tailscale HTTPS name
	@bash $(SERVE) tunnel

url: ## Print the URL to open in the Quest Browser
	@bash $(SERVE) url

up: start tunnel ## build + start + tunnel

untunnel: ## Remove the Tailscale route
	@bash $(SERVE) untunnel

stop: ## Stop the background server
	@bash $(SERVE) stop

down: untunnel stop ## untunnel + stop

status: ## Server health and Tailscale routes
	@bash $(SERVE) status

logs: ## Follow the server log
	tail -f .run/server-$(PORT).log
