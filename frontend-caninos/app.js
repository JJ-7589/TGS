import {
  apiRequest,
  checkBackendHealth,
  getToken,
  getCurrentUser,
  setSession,
  updateCurrentUser,
  clearSession,
} from "./apiClient.js";

// ==================== ESTADO EN MEMORIA ====================
const state = {
  allClientes: [],
  clientes: [],
  allMascotas: [],
  mascotas: [],
  consultas: [],
};

// ==================== REFERENCIAS DEL DOM ====================
const authSection = document.getElementById("auth-section");
const dashboardSection = document.getElementById("dashboard-section");
const loginForm = document.getElementById("login-form");
const registerForm = document.getElementById("register-form");
const loginError = document.getElementById("login-error");
const registerError = document.getElementById("register-error");
const authTitle = document.getElementById("auth-title");
const authSubtitle = document.getElementById("auth-subtitle");
const toggleAuthText = document.getElementById("toggle-auth-text");
const btnToggleAuth = document.getElementById("btn-toggle-auth");
const currentUserLabel = document.getElementById("current-user-label");
const btnLogout = document.getElementById("btn-logout");
const backendStatusEl = document.getElementById("backend-status");

// Pestañas
const tabButtons = document.querySelectorAll(".tab-btn");
const tabPanels = document.querySelectorAll(".tab-panel");

// Clientes
const tbodyClientes = document.getElementById("tbody-clientes");
const searchClientesInput = document.getElementById("search-clientes");

// Mascotas
const tbodyMascotas = document.getElementById("tbody-mascotas");
const filterMascotaCliente = document.getElementById("filter-mascota-cliente");

// Consultas
const tbodyConsultas = document.getElementById("tbody-consultas");
const filterConsultaCliente = document.getElementById("filter-consulta-cliente");
const filterConsultaMascota = document.getElementById("filter-consulta-mascota");
const filterConsultaServicio = document.getElementById("filter-consulta-servicio");
const filterConsultaEstado = document.getElementById("filter-consulta-estado");

// Modal y Toast
const modalOverlay = document.getElementById("modal-overlay");
const modalTitle = document.getElementById("modal-title");
const modalBody = document.getElementById("modal-body");
const btnCloseModal = document.getElementById("btn-close-modal");
const toastEl = document.getElementById("toast");

// ==================== UTILIDADES ====================
let toastTimer = null;

function showToast(message, type = "info") {
  clearTimeout(toastTimer);
  toastEl.textContent = message;
  toastEl.className = `toast ${type}`;
  toastTimer = setTimeout(() => {
    toastEl.className = "toast hidden";
  }, 3200);
}

function escapeHtml(value) {
  if (value === null || value === undefined) return "";
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function renderApiError(container, err) {
  container.classList.remove("hidden");
  const fieldEntries = Object.entries(err.errors || {});

  if (fieldEntries.length === 0) {
    container.innerHTML = `<strong>${escapeHtml(err.message)}</strong>`;
    return;
  }

  const items = fieldEntries
    .flatMap(([field, messages]) =>
      (Array.isArray(messages) ? messages : [messages]).map(
        (msg) => `<li><strong>${escapeHtml(field)}:</strong> ${escapeHtml(msg)}</li>`
      )
    )
    .join("");

  container.innerHTML = `
    <strong>${escapeHtml(err.message)}</strong>
    <ul>${items}</ul>
  `;
}

function hideError(container) {
  container.classList.add("hidden");
  container.innerHTML = "";
}

function formatDate(isoString) {
  if (!isoString) return "-";
  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) return isoString;
  return date.toLocaleString("es-CO", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function toDatetimeLocalValue(isoString) {
  const d = isoString ? new Date(isoString) : new Date();
  if (Number.isNaN(d.getTime())) return "";
  const offsetMs = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - offsetMs).toISOString().slice(0, 16);
}

function formatCurrency(amount) {
  const num = Number(amount ?? 0);
  return new Intl.NumberFormat("es-CO", {
    style: "currency",
    currency: "COP",
    maximumFractionDigits: 2,
  }).format(num);
}

function isValidNumericPassword(pwd) {
  return /^\d{5,20}$/.test(pwd);
}

// ==================== NAVEGACIÓN DE VISTAS Y RUTAS ====================
let isRegisterMode = false;
const useCleanRoutes =
  window.location.protocol.startsWith("http") &&
  (window.location.hostname === "localhost" || window.location.hostname === "127.0.0.1") &&
  window.location.port === "5150";

function navigatePath(targetPath, { replace = false } = {}) {
  if (!useCleanRoutes || window.location.pathname === targetPath) return;
  if (replace) {
    window.history.replaceState(null, "", targetPath);
  } else {
    window.history.pushState(null, "", targetPath);
  }
}

function applyAuthMode(registerMode, { syncUrl = true } = {}) {
  isRegisterMode = registerMode;
  hideError(loginError);
  hideError(registerError);
  loginForm.classList.toggle("hidden", isRegisterMode);
  registerForm.classList.toggle("hidden", !isRegisterMode);
  updateToggleAuthLabel();
  if (syncUrl) {
    navigatePath(isRegisterMode ? "/registro" : "/login");
  }
}

function updateToggleAuthLabel() {
  if (isRegisterMode) {
    authTitle.textContent = "Crear Cuenta";
    authSubtitle.textContent = "Registra un nuevo usuario para acceder al sistema";
    toggleAuthText.innerHTML =
      '¿Ya tienes cuenta? <button type="button" id="btn-toggle-auth" class="btn-link">Inicia sesión</button>';
  } else {
    authTitle.textContent = "Iniciar Sesión";
    authSubtitle.textContent = "Ingresa con tus credenciales para administrar el sistema";
    toggleAuthText.innerHTML =
      '¿No tienes cuenta? <button type="button" id="btn-toggle-auth" class="btn-link">Regístrate aquí</button>';
  }
  document.getElementById("btn-toggle-auth").addEventListener("click", toggleAuthMode);
}

function toggleAuthMode() {
  applyAuthMode(!isRegisterMode);
}

btnToggleAuth.addEventListener("click", toggleAuthMode);

let sessionExpiredHandled = false;

async function refreshBackendStatus() {
  if (!backendStatusEl) return;
  try {
    await checkBackendHealth();
    backendStatusEl.textContent = "🟢 Backend y SQLite conectados";
    backendStatusEl.className = "status-pill status-online";
  } catch {
    backendStatusEl.textContent = "🔴 Backend desconectado (inicia el servidor en puerto 5150)";
    backendStatusEl.className = "status-pill status-offline";
  }
}

async function showDashboard({ replaceUrl = false } = {}) {
  sessionExpiredHandled = false;
  const user = getCurrentUser();
  currentUserLabel.textContent = user
    ? `${user.nombreCompleto} (@${user.username})`
    : "Sesión activa";

  authSection.classList.add("hidden");
  dashboardSection.classList.remove("hidden");
  navigatePath("/dashboard", { replace: replaceUrl });

  await Promise.all([loadClientes(), loadMascotas(), loadConsultas()]);
}

function showAuth({ register = false, replaceUrl = true } = {}) {
  clearSession();
  dashboardSection.classList.add("hidden");
  authSection.classList.remove("hidden");
  loginForm.reset();
  registerForm.reset();
  applyAuthMode(register, { syncUrl: false });
  navigatePath(register ? "/registro" : "/login", { replace: replaceUrl });
  refreshBackendStatus();
}

window.addEventListener("popstate", () => {
  if (!useCleanRoutes) return;
  const path = window.location.pathname.toLowerCase();
  if (getToken()) {
    if (path === "/login" || path === "/registro") {
      navigatePath("/dashboard", { replace: true });
    }
    return;
  }
  applyAuthMode(path === "/registro", { syncUrl: false });
});

window.addEventListener("session-expired", () => {
  if (sessionExpiredHandled) return;
  sessionExpiredHandled = true;
  showAuth();
  showToast("Tu sesión expiró o no es válida. Inicia sesión nuevamente.", "error");
});

btnLogout.addEventListener("click", () => {
  showAuth();
  showToast("Sesión cerrada.", "info");
});

// ==================== AUTENTICACIÓN ====================
loginForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideError(loginError);

  const username = document.getElementById("login-username").value.trim();
  const password = document.getElementById("login-password").value;

  if (!isValidNumericPassword(password)) {
    renderApiError(loginError, {
      message: "Error de validación local.",
      errors: {
        password: ["La contraseña debe contener solo dígitos (0-9) y tener entre 5 y 20 caracteres."],
      },
    });
    return;
  }

  try {
    const authData = await apiRequest("/auth/login", {
      method: "POST",
      auth: false,
      body: { username, password },
    });
    setSession(authData);
    showToast(`Bienvenido, ${authData.usuario.nombreCompleto}`, "success");
    await showDashboard();
  } catch (err) {
    renderApiError(loginError, err);
  }
});

registerForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideError(registerError);

  const payload = {
    username: document.getElementById("reg-username").value.trim(),
    password: document.getElementById("reg-password").value,
    documento: document.getElementById("reg-documento").value.trim(),
    nombreCompleto: document.getElementById("reg-nombre").value.trim(),
    telefono: document.getElementById("reg-telefono").value.trim(),
    email: document.getElementById("reg-email").value.trim(),
    direccion: document.getElementById("reg-direccion").value.trim(),
  };

  if (!isValidNumericPassword(payload.password)) {
    renderApiError(registerError, {
      message: "Error de validación local.",
      errors: {
        password: ["La contraseña debe contener solo dígitos (0-9) y tener entre 5 y 20 caracteres."],
      },
    });
    return;
  }

  try {
    const authData = await apiRequest("/auth/register", {
      method: "POST",
      auth: false,
      body: payload,
    });
    setSession(authData);
    showToast("Cuenta creada exitosamente.", "success");
    await showDashboard();
  } catch (err) {
    renderApiError(registerError, err);
  }
});

// ==================== PESTAÑAS ====================
tabButtons.forEach((btn) => {
  btn.addEventListener("click", () => {
    const targetTab = btn.dataset.tab;
    tabButtons.forEach((b) => b.classList.toggle("active", b === btn));
    tabPanels.forEach((panel) => {
      panel.classList.toggle("hidden", panel.id !== `tab-${targetTab}`);
    });
  });
});

// ==================== MODAL ====================
function openModal(title, htmlContent) {
  modalTitle.textContent = title;
  modalBody.innerHTML = htmlContent;
  modalOverlay.classList.remove("hidden");
}

function closeModal() {
  modalOverlay.classList.add("hidden");
  modalBody.innerHTML = "";
}

btnCloseModal.addEventListener("click", closeModal);
modalOverlay.addEventListener("click", (e) => {
  if (e.target === modalOverlay) closeModal();
});

// ==================== SELECTORES AUXILIARES ====================
function populateClienteSelects() {
  const prevMascotaFilter = filterMascotaCliente.value;
  const prevConsultaFilter = filterConsultaCliente.value;

  const optionsHtml = state.allClientes
    .map(
      (c) =>
        `<option value="${c.id}">${escapeHtml(c.nombreCompleto)} (${escapeHtml(c.documento)})</option>`
    )
    .join("");

  filterMascotaCliente.innerHTML =
    `<option value="">Todos los clientes (propietarios)</option>${optionsHtml}`;
  filterConsultaCliente.innerHTML =
    `<option value="">Filtrar por Cliente...</option>${optionsHtml}`;

  if (prevMascotaFilter) filterMascotaCliente.value = prevMascotaFilter;
  if (prevConsultaFilter) filterConsultaCliente.value = prevConsultaFilter;
}

function populateConsultaMascotaFilter(userIdFilter = "") {
  const prevValue = filterConsultaMascota.value;
  const list = userIdFilter
    ? state.allMascotas.filter((m) => String(m.userId) === String(userIdFilter))
    : state.allMascotas;

  filterConsultaMascota.innerHTML =
    `<option value="">Filtrar por Canino...</option>` +
    list
      .map(
        (m) =>
          `<option value="${m.id}">${escapeHtml(m.nombre)} (${escapeHtml(m.raza)})</option>`
      )
      .join("");

  if (list.some((m) => String(m.id) === String(prevValue))) {
    filterConsultaMascota.value = prevValue;
  }
}

filterConsultaCliente.addEventListener("change", () => {
  populateConsultaMascotaFilter(filterConsultaCliente.value);
});

// =========================================================
// MÓDULO 1: CLIENTES (/api/usuarios)
// =========================================================
async function loadClientes(search = "") {
  try {
    const trimmed = search.trim();
    const query = trimmed ? `?search=${encodeURIComponent(trimmed)}` : "";
    const data = await apiRequest(`/usuarios${query}`);
    state.clientes = Array.isArray(data) ? data : [];
    if (!trimmed) {
      state.allClientes = state.clientes;
    } else if (state.allClientes.length === 0) {
      const full = await apiRequest("/usuarios");
      state.allClientes = Array.isArray(full) ? full : [];
    }
    renderClientesTable();
    populateClienteSelects();
  } catch (err) {
    showToast(err.message, "error");
  }
}

async function refreshClientesCatalog() {
  try {
    const full = await apiRequest("/usuarios");
    state.allClientes = Array.isArray(full) ? full : [];
    populateClienteSelects();
  } catch {
    // Manejado por apiRequest si la sesión expiró
  }
}

function renderClientesTable() {
  if (state.clientes.length === 0) {
    tbodyClientes.innerHTML = `<tr><td colspan="7" class="empty-row">No hay clientes registrados.</td></tr>`;
    return;
  }

  tbodyClientes.innerHTML = state.clientes
    .map(
      (c) => `
      <tr>
        <td>${c.id}</td>
        <td><strong>${escapeHtml(c.username)}</strong></td>
        <td>${escapeHtml(c.documento)}</td>
        <td>${escapeHtml(c.nombreCompleto)}</td>
        <td>${escapeHtml(c.telefono)}</td>
        <td>${escapeHtml(c.email)}</td>
        <td class="actions-cell">
          <button type="button" class="btn btn-outline btn-sm" data-edit-cliente="${c.id}">Editar</button>
          <button type="button" class="btn btn-danger btn-sm" data-del-cliente="${c.id}">Eliminar</button>
        </td>
      </tr>
    `
    )
    .join("");
}

document.getElementById("btn-search-clientes").addEventListener("click", () => {
  loadClientes(searchClientesInput.value);
});

document.getElementById("btn-reset-clientes").addEventListener("click", () => {
  searchClientesInput.value = "";
  loadClientes("");
});

document.getElementById("btn-new-cliente").addEventListener("click", () => {
  openClienteModal(null);
});

function openClienteModal(cliente) {
  const isEdit = Boolean(cliente);
  openModal(
    isEdit ? `Editar Cliente #${cliente.id}` : "Registrar Nuevo Cliente",
    `
    <form id="cliente-modal-form" class="form-stack">
      <div class="form-grid-2">
        <div class="form-group">
          <label for="cli-username">Usuario *</label>
          <input type="text" id="cli-username" value="${escapeHtml(cliente?.username || "")}" required />
        </div>
        <div class="form-group">
          <label for="cli-password">
            Contraseña numérica ${isEdit ? "(opcional)" : "*"}
          </label>
          <input
            type="password"
            id="cli-password"
            inputmode="numeric"
            placeholder="${isEdit ? "Dejar vacío para conservar" : "Ej: 12345"}"
            ${isEdit ? "" : "required"}
          />
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="cli-documento">Documento *</label>
          <input type="text" id="cli-documento" value="${escapeHtml(cliente?.documento || "")}" required />
        </div>
        <div class="form-group">
          <label for="cli-telefono">Teléfono *</label>
          <input type="text" id="cli-telefono" value="${escapeHtml(cliente?.telefono || "")}" required />
        </div>
      </div>

      <div class="form-group">
        <label for="cli-nombre">Nombre Completo *</label>
        <input type="text" id="cli-nombre" value="${escapeHtml(cliente?.nombreCompleto || "")}" required />
      </div>

      <div class="form-group">
        <label for="cli-email">Correo Electrónico *</label>
        <input type="email" id="cli-email" value="${escapeHtml(cliente?.email || "")}" required />
      </div>

      <div class="form-group">
        <label for="cli-direccion">Dirección *</label>
        <input type="text" id="cli-direccion" value="${escapeHtml(cliente?.direccion || "")}" required />
      </div>

      <div id="cliente-modal-error" class="form-error hidden"></div>

      <button type="submit" class="btn btn-primary btn-block">
        ${isEdit ? "Guardar Cambios" : "Registrar Cliente"}
      </button>
    </form>
  `
  );

  const form = document.getElementById("cliente-modal-form");
  const errBox = document.getElementById("cliente-modal-error");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    hideError(errBox);

    const rawPwd = document.getElementById("cli-password").value;

    if (!isEdit || rawPwd.length > 0) {
      if (!isValidNumericPassword(rawPwd)) {
        renderApiError(errBox, {
          message: "Error de validación local.",
          errors: {
            password: ["La contraseña debe ser numérica (0-9) y tener entre 5 y 20 dígitos."],
          },
        });
        return;
      }
    }

    const payload = {
      username: document.getElementById("cli-username").value.trim(),
      documento: document.getElementById("cli-documento").value.trim(),
      nombreCompleto: document.getElementById("cli-nombre").value.trim(),
      telefono: document.getElementById("cli-telefono").value.trim(),
      email: document.getElementById("cli-email").value.trim(),
      direccion: document.getElementById("cli-direccion").value.trim(),
    };

    if (!isEdit) {
      payload.password = rawPwd;
    } else if (rawPwd.length > 0) {
      payload.password = rawPwd;
    }

    try {
      if (isEdit) {
        const updated = await apiRequest(`/usuarios/${cliente.id}`, { method: "PUT", body: payload });
        const currentUser = getCurrentUser();
        if (currentUser && String(currentUser.id) === String(updated.id)) {
          updateCurrentUser(updated);
          currentUserLabel.textContent = `${updated.nombreCompleto} (@${updated.username})`;
        }
        showToast("Cliente actualizado correctamente.", "success");
      } else {
        await apiRequest("/usuarios", { method: "POST", body: payload });
        showToast("Cliente registrado correctamente.", "success");
      }
      closeModal();
      await refreshClientesCatalog();
      await Promise.all([
        loadClientes(searchClientesInput.value),
        loadMascotas(filterMascotaCliente.value),
        loadConsultas(),
      ]);
    } catch (err) {
      renderApiError(errBox, err);
    }
  });
}

tbodyClientes.addEventListener("click", async (e) => {
  const editId = e.target.dataset.editCliente;
  const delId = e.target.dataset.delCliente;

  if (editId) {
    const cliente = state.clientes.find((c) => String(c.id) === String(editId));
    if (cliente) openClienteModal(cliente);
  }

  if (delId) {
    if (!confirm(`¿Deseas eliminar al cliente #${delId}? (Si tiene caninos o consultas asociadas, el servidor lo bloqueará).`)) {
      return;
    }
    try {
      await apiRequest(`/usuarios/${delId}`, { method: "DELETE" });
      showToast("Cliente eliminado.", "success");
      await refreshClientesCatalog();
      await loadClientes(searchClientesInput.value);
    } catch (err) {
      showToast(err.message, "error");
    }
  }
});

// =========================================================
// MÓDULO 2: CANINOS / MASCOTAS (/api/mascotas)
// =========================================================
async function loadMascotas(userId = "") {
  try {
    const query = userId ? `?userId=${encodeURIComponent(userId)}` : "";
    const data = await apiRequest(`/mascotas${query}`);
    state.mascotas = Array.isArray(data) ? data : [];
    if (!userId) {
      state.allMascotas = state.mascotas;
    } else if (state.allMascotas.length === 0) {
      const full = await apiRequest("/mascotas");
      state.allMascotas = Array.isArray(full) ? full : [];
    }
    renderMascotasTable();
    populateConsultaMascotaFilter(filterConsultaCliente.value);
  } catch (err) {
    showToast(err.message, "error");
  }
}

async function refreshMascotasCatalog() {
  try {
    const full = await apiRequest("/mascotas");
    state.allMascotas = Array.isArray(full) ? full : [];
    populateConsultaMascotaFilter(filterConsultaCliente.value);
  } catch {
    // Manejado por apiRequest si la sesión expiró
  }
}

function renderMascotasTable() {
  if (state.mascotas.length === 0) {
    tbodyMascotas.innerHTML = `<tr><td colspan="9" class="empty-row">No hay caninos registrados.</td></tr>`;
    return;
  }

  tbodyMascotas.innerHTML = state.mascotas
    .map(
      (m) => `
      <tr>
        <td>${m.id}</td>
        <td><strong>${escapeHtml(m.nombre)}</strong></td>
        <td>${escapeHtml(m.raza)}</td>
        <td>${m.edad} ${m.edad === 1 ? "año" : "años"}</td>
        <td>${m.pesoKg} kg</td>
        <td>${escapeHtml(m.sexo)}</td>
        <td>${m.esterilizado ? "Sí" : "No"}</td>
        <td>${escapeHtml(m.propietarioNombre || `ID ${m.userId}`)}</td>
        <td class="actions-cell">
          <button type="button" class="btn btn-outline btn-sm" data-edit-mascota="${m.id}">Editar</button>
          <button type="button" class="btn btn-danger btn-sm" data-del-mascota="${m.id}">Eliminar</button>
        </td>
      </tr>
    `
    )
    .join("");
}

document.getElementById("btn-filter-mascotas").addEventListener("click", () => {
  loadMascotas(filterMascotaCliente.value);
});

document.getElementById("btn-reset-mascotas").addEventListener("click", () => {
  filterMascotaCliente.value = "";
  loadMascotas("");
});

document.getElementById("btn-new-mascota").addEventListener("click", () => {
  if (state.allClientes.length === 0) {
    showToast("Primero debes registrar al menos un cliente.", "error");
    return;
  }
  openMascotaModal(null);
});

function openMascotaModal(mascota) {
  const isEdit = Boolean(mascota);
  const clienteOptions = state.allClientes
    .map(
      (c) =>
        `<option value="${c.id}" ${
          mascota && String(mascota.userId) === String(c.id) ? "selected" : ""
        }>${escapeHtml(c.nombreCompleto)} (${escapeHtml(c.documento)})</option>`
    )
    .join("");

  openModal(
    isEdit ? `Editar Canino #${mascota.id}` : "Registrar Nuevo Canino",
    `
    <form id="mascota-modal-form" class="form-stack">
      <div class="form-group">
        <label for="mas-userId">Cliente Propietario *</label>
        <select id="mas-userId" required>
          <option value="">Selecciona un cliente...</option>
          ${clienteOptions}
        </select>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="mas-nombre">Nombre *</label>
          <input type="text" id="mas-nombre" value="${escapeHtml(mascota?.nombre || "")}" required />
        </div>
        <div class="form-group">
          <label for="mas-raza">Raza *</label>
          <input type="text" id="mas-raza" value="${escapeHtml(mascota?.raza || "")}" required />
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="mas-edad">Edad (0 - 30 años) *</label>
          <input type="number" id="mas-edad" min="0" max="30" step="1" value="${mascota?.edad ?? ""}" required />
        </div>
        <div class="form-group">
          <label for="mas-pesoKg">Peso (0.2 - 120 kg) *</label>
          <input type="number" id="mas-pesoKg" min="0.2" max="120" step="0.01" value="${mascota?.pesoKg ?? ""}" required />
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="mas-sexo">Sexo *</label>
          <select id="mas-sexo" required>
            <option value="Macho" ${mascota?.sexo === "Macho" ? "selected" : ""}>Macho</option>
            <option value="Hembra" ${mascota?.sexo === "Hembra" ? "selected" : ""}>Hembra</option>
          </select>
        </div>
        <div class="form-group">
          <label for="mas-color">Color *</label>
          <input type="text" id="mas-color" value="${escapeHtml(mascota?.color || "")}" required />
        </div>
      </div>

      <div class="form-group checkbox-row">
        <input type="checkbox" id="mas-esterilizado" ${mascota?.esterilizado ? "checked" : ""} />
        <label for="mas-esterilizado">Esterilizado</label>
      </div>

      <div class="form-group">
        <label for="mas-observaciones">Observaciones</label>
        <textarea id="mas-observaciones" rows="2">${escapeHtml(mascota?.observaciones || "")}</textarea>
      </div>

      <div id="mascota-modal-error" class="form-error hidden"></div>

      <button type="submit" class="btn btn-primary btn-block">
        ${isEdit ? "Guardar Cambios" : "Registrar Canino"}
      </button>
    </form>
  `
  );

  const form = document.getElementById("mascota-modal-form");
  const errBox = document.getElementById("mascota-modal-error");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    hideError(errBox);

    const obsVal = document.getElementById("mas-observaciones").value.trim();

    const payload = {
      userId: parseInt(document.getElementById("mas-userId").value, 10),
      nombre: document.getElementById("mas-nombre").value.trim(),
      raza: document.getElementById("mas-raza").value.trim(),
      edad: parseInt(document.getElementById("mas-edad").value, 10),
      pesoKg: parseFloat(document.getElementById("mas-pesoKg").value),
      sexo: document.getElementById("mas-sexo").value,
      color: document.getElementById("mas-color").value.trim(),
      esterilizado: Boolean(document.getElementById("mas-esterilizado").checked),
      observaciones: obsVal.length > 0 ? obsVal : null,
    };

    try {
      if (isEdit) {
        await apiRequest(`/mascotas/${mascota.id}`, { method: "PUT", body: payload });
        showToast("Canino actualizado correctamente.", "success");
      } else {
        await apiRequest("/mascotas", { method: "POST", body: payload });
        showToast("Canino registrado correctamente.", "success");
      }
      closeModal();
      await refreshMascotasCatalog();
      await Promise.all([
        loadMascotas(filterMascotaCliente.value),
        loadConsultas(),
      ]);
    } catch (err) {
      renderApiError(errBox, err);
    }
  });
}

tbodyMascotas.addEventListener("click", async (e) => {
  const editId = e.target.dataset.editMascota;
  const delId = e.target.dataset.delMascota;

  if (editId) {
    const mascota = state.mascotas.find((m) => String(m.id) === String(editId));
    if (mascota) openMascotaModal(mascota);
  }

  if (delId) {
    if (!confirm(`¿Deseas eliminar al canino #${delId}?`)) return;
    try {
      await apiRequest(`/mascotas/${delId}`, { method: "DELETE" });
      showToast("Canino eliminado.", "success");
      await refreshMascotasCatalog();
      await loadMascotas(filterMascotaCliente.value);
    } catch (err) {
      showToast(err.message, "error");
    }
  }
});

// =========================================================
// MÓDULO 3: CONSULTAS Y SERVICIOS (/api/consultas)
// =========================================================
async function loadConsultas() {
  const userId = filterConsultaCliente.value;
  const mascotaId = filterConsultaMascota.value;
  const tipoServicio = filterConsultaServicio.value;
  const estado = filterConsultaEstado.value;

  let endpoint = "/consultas";
  const params = new URLSearchParams();

  if (userId) params.set("userId", userId);
  if (mascotaId) params.set("mascotaId", mascotaId);
  if (tipoServicio) params.set("tipoServicio", tipoServicio);
  if (estado) params.set("estado", estado);

  // Si solo hay un filtro de usuario, mascota o servicio, podemos usar las rutas directas o query string
  if (!estado && userId && !mascotaId && !tipoServicio) {
    endpoint = `/consultas/usuario/${userId}`;
  } else if (!estado && mascotaId && !userId && !tipoServicio) {
    endpoint = `/consultas/mascota/${mascotaId}`;
  } else if (!estado && tipoServicio && !userId && !mascotaId) {
    endpoint = `/consultas/servicio/${encodeURIComponent(tipoServicio)}`;
  } else if ([...params.keys()].length > 0) {
    endpoint = `/consultas?${params.toString()}`;
  }

  try {
    const data = await apiRequest(endpoint);
    state.consultas = Array.isArray(data) ? data : [];
    renderConsultasTable();
  } catch (err) {
    showToast(err.message, "error");
  }
}

function renderConsultasTable() {
  if (state.consultas.length === 0) {
    tbodyConsultas.innerHTML = `<tr><td colspan="9" class="empty-row">No hay consultas registradas para el filtro actual.</td></tr>`;
    return;
  }

  tbodyConsultas.innerHTML = state.consultas
    .map((c) => {
      const srvKey = String(c.tipoServicio || "").toLowerCase();
      const estKey = String(c.estado || "").toLowerCase();
      return `
        <tr>
          <td>${c.id}</td>
          <td>${escapeHtml(formatDate(c.fechaConsulta))}</td>
          <td><span class="badge badge-${srvKey}">${escapeHtml(c.tipoServicio)}</span></td>
          <td>${escapeHtml(c.mascotaNombre || `ID ${c.mascotaId}`)}</td>
          <td>${escapeHtml(c.usuarioNombre || `ID ${c.userId}`)}</td>
          <td>${escapeHtml(c.descripcion)}</td>
          <td>${escapeHtml(formatCurrency(c.costo))}</td>
          <td><span class="badge badge-${estKey}">${escapeHtml(c.estado)}</span></td>
          <td class="actions-cell">
            <button type="button" class="btn btn-outline btn-sm" data-edit-consulta="${c.id}">Editar</button>
            <button type="button" class="btn btn-danger btn-sm" data-del-consulta="${c.id}">Eliminar</button>
          </td>
        </tr>
      `;
    })
    .join("");
}

document.getElementById("btn-filter-consultas").addEventListener("click", () => {
  loadConsultas();
});

document.getElementById("btn-reset-consultas").addEventListener("click", () => {
  filterConsultaCliente.value = "";
  filterConsultaMascota.value = "";
  filterConsultaServicio.value = "";
  filterConsultaEstado.value = "";
  populateConsultaMascotaFilter("");
  loadConsultas();
});

document.getElementById("btn-new-consulta").addEventListener("click", () => {
  if (state.allClientes.length === 0 || state.allMascotas.length === 0) {
    showToast("Necesitas al menos un cliente y un canino registrado para crear una consulta.", "error");
    return;
  }
  openConsultaModal(null);
});

function openConsultaModal(consulta) {
  const isEdit = Boolean(consulta);

  const clienteOptions = state.allClientes
    .map(
      (c) =>
        `<option value="${c.id}" ${
          consulta && String(consulta.userId) === String(c.id) ? "selected" : ""
        }>${escapeHtml(c.nombreCompleto)}</option>`
    )
    .join("");

  openModal(
    isEdit ? `Editar Consulta #${consulta.id}` : "Registrar Nueva Consulta",
    `
    <form id="consulta-modal-form" class="form-stack">
      <div class="form-grid-2">
        <div class="form-group">
          <label for="con-userId">Cliente *</label>
          <select id="con-userId" required>
            <option value="">Selecciona un cliente...</option>
            ${clienteOptions}
          </select>
        </div>

        <div class="form-group">
          <label for="con-mascotaId">Canino del Cliente *</label>
          <select id="con-mascotaId" required>
            <option value="">Selecciona un canino...</option>
          </select>
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="con-tipoServicio">Tipo de Servicio *</label>
          <select id="con-tipoServicio" required>
            <option value="Salud" ${consulta?.tipoServicio === "Salud" ? "selected" : ""}>Salud</option>
            <option value="Estetica" ${consulta?.tipoServicio === "Estetica" ? "selected" : ""}>Estética</option>
            <option value="Recreacion" ${consulta?.tipoServicio === "Recreacion" ? "selected" : ""}>Recreación</option>
          </select>
        </div>

        <div class="form-group">
          <label for="con-estado">Estado *</label>
          <select id="con-estado" required>
            <option value="Programada" ${consulta?.estado === "Programada" ? "selected" : ""}>Programada</option>
            <option value="EnProceso" ${consulta?.estado === "EnProceso" ? "selected" : ""}>En Proceso</option>
            <option value="Completada" ${consulta?.estado === "Completada" ? "selected" : ""}>Completada</option>
            <option value="Cancelada" ${consulta?.estado === "Cancelada" ? "selected" : ""}>Cancelada</option>
          </select>
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="con-fechaConsulta">Fecha y Hora de Consulta *</label>
          <input
            type="datetime-local"
            id="con-fechaConsulta"
            value="${toDatetimeLocalValue(consulta?.fechaConsulta)}"
            required
          />
        </div>

        <div class="form-group">
          <label for="con-costo">Costo (COP) *</label>
          <input
            type="number"
            id="con-costo"
            min="0"
            max="100000000"
            step="0.01"
            value="${consulta?.costo ?? ""}"
            required
          />
        </div>
      </div>

      <div class="form-group">
        <label for="con-descripcion">Descripción (mín. 5 caracteres) *</label>
        <textarea id="con-descripcion" rows="2" required minlength="5" maxlength="500">${escapeHtml(
          consulta?.descripcion || ""
        )}</textarea>
      </div>

      <div class="form-group">
        <label for="con-observaciones">Observaciones</label>
        <textarea id="con-observaciones" rows="2" maxlength="500">${escapeHtml(
          consulta?.observaciones || ""
        )}</textarea>
      </div>

      <div id="consulta-modal-error" class="form-error hidden"></div>

      <button type="submit" class="btn btn-primary btn-block">
        ${isEdit ? "Guardar Cambios" : "Registrar Consulta"}
      </button>
    </form>
  `
  );

  const selectCliente = document.getElementById("con-userId");
  const selectMascota = document.getElementById("con-mascotaId");

  function syncMascotasForCliente(selectedMascotaId = null) {
    const uid = selectCliente.value;
    const ownerMascotas = state.allMascotas.filter(
      (m) => String(m.userId) === String(uid)
    );

    selectMascota.innerHTML =
      `<option value="">${
        ownerMascotas.length ? "Selecciona un canino..." : "Este cliente no tiene caninos"
      }</option>` +
      ownerMascotas
        .map(
          (m) =>
            `<option value="${m.id}" ${
              selectedMascotaId && String(selectedMascotaId) === String(m.id)
                ? "selected"
                : ""
            }>${escapeHtml(m.nombre)} (${escapeHtml(m.raza)})</option>`
        )
        .join("");
  }

  selectCliente.addEventListener("change", () => syncMascotasForCliente(null));
  syncMascotasForCliente(consulta?.mascotaId || null);

  const form = document.getElementById("consulta-modal-form");
  const errBox = document.getElementById("consulta-modal-error");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    hideError(errBox);

    const localDateStr = document.getElementById("con-fechaConsulta").value;
    const parsedDate = new Date(localDateStr);
    if (!localDateStr || Number.isNaN(parsedDate.getTime())) {
      renderApiError(errBox, {
        message: "Error de validación local.",
        errors: { fechaConsulta: ["Debes seleccionar una fecha y hora válidas."] },
      });
      return;
    }
    const fechaIso = parsedDate.toISOString();
    const obsVal = document.getElementById("con-observaciones").value.trim();

    const payload = {
      userId: parseInt(selectCliente.value, 10),
      mascotaId: parseInt(selectMascota.value, 10),
      tipoServicio: document.getElementById("con-tipoServicio").value,
      descripcion: document.getElementById("con-descripcion").value.trim(),
      fechaConsulta: fechaIso,
      costo: parseFloat(document.getElementById("con-costo").value),
      estado: document.getElementById("con-estado").value,
      observaciones: obsVal.length > 0 ? obsVal : null,
    };

    try {
      if (isEdit) {
        await apiRequest(`/consultas/${consulta.id}`, { method: "PUT", body: payload });
        showToast("Consulta actualizada correctamente.", "success");
      } else {
        await apiRequest("/consultas", { method: "POST", body: payload });
        showToast("Consulta registrada correctamente.", "success");
      }
      closeModal();
      await loadConsultas();
    } catch (err) {
      renderApiError(errBox, err);
    }
  });
}

tbodyConsultas.addEventListener("click", async (e) => {
  const editId = e.target.dataset.editConsulta;
  const delId = e.target.dataset.delConsulta;

  if (editId) {
    const consulta = state.consultas.find((c) => String(c.id) === String(editId));
    if (consulta) openConsultaModal(consulta);
  }

  if (delId) {
    if (!confirm(`¿Deseas eliminar la consulta #${delId}?`)) return;
    try {
      await apiRequest(`/consultas/${delId}`, { method: "DELETE" });
      showToast("Consulta eliminada.", "success");
      await loadConsultas();
    } catch (err) {
      showToast(err.message, "error");
    }
  }
});

// ==================== ARRANQUE INICIAL ====================
const initialPath = window.location.pathname.toLowerCase();
if (getToken()) {
  showDashboard({ replaceUrl: true });
} else {
  showAuth({ register: initialPath === "/registro", replaceUrl: true });
}