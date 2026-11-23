import {
  apiRequest,
  uploadMascotaPhoto,
  resolveAssetUrl,
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
  selectedMascotaId: null, // Al hacer clic en una mascota del grid, se centra igual que cuando hay 1 sola
};

function isAdminUser() {
  const user = getCurrentUser();
  return String(user?.rol || "").toLowerCase() === "admin";
}

// ==================== REFERENCIAS DEL DOM ====================
const authSection = document.getElementById("auth-section");
const dashboardSection = document.getElementById("dashboard-section");
const dashboardMain = document.getElementById("dashboard-main");
const perfilSection = document.getElementById("perfil-section");
const tabsNav = document.getElementById("tabs-nav");

const loginForm = document.getElementById("login-form");
const registerForm = document.getElementById("register-form");
const loginError = document.getElementById("login-error");
const registerError = document.getElementById("register-error");
const authTitle = document.getElementById("auth-title");
const authSubtitle = document.getElementById("auth-subtitle");
const toggleAuthText = document.getElementById("toggle-auth-text");
const btnToggleAuth = document.getElementById("btn-toggle-auth");

const topbarHome = document.getElementById("topbar-home");
const currentUserLabel = document.getElementById("current-user-label");
const currentRoleBadge = document.getElementById("current-role-badge");
const btnOpenPerfil = document.getElementById("btn-open-perfil");
const btnBackDashboard = document.getElementById("btn-back-dashboard");
const btnLogout = document.getElementById("btn-logout");

// Pestañas
const tabButtons = document.querySelectorAll(".tab-btn");
const tabPanels = document.querySelectorAll(".tab-panel");
const tabBtnClientes = document.getElementById("tab-btn-clientes");
const tabBtnMascotas = document.getElementById("tab-btn-mascotas");
const tabBtnConsultas = document.getElementById("tab-btn-consultas");

// Clientes (admin)
const tbodyClientes = document.getElementById("tbody-clientes");
const searchClientesInput = document.getElementById("search-clientes");

// Mascotas (Grid + Tarjeta Centrada)
const mascotasHeading = document.getElementById("mascotas-heading");
const mascotasSubtitle = document.getElementById("mascotas-subtitle");
const mascotasAdminFilter = document.getElementById("mascotas-admin-filter");
const filterMascotaCliente = document.getElementById("filter-mascota-cliente");
const mascotasViewContainer = document.getElementById("mascotas-view-container");

// Consultas
const consultasHeading = document.getElementById("consultas-heading");
const tbodyConsultas = document.getElementById("tbody-consultas");
const filterConsultaCliente = document.getElementById("filter-consulta-cliente");
const filterConsultaMascota = document.getElementById("filter-consulta-mascota");
const filterConsultaServicio = document.getElementById("filter-consulta-servicio");
const filterConsultaEstado = document.getElementById("filter-consulta-estado");

// Perfil
const perfilDatosForm = document.getElementById("perfil-datos-form");
const perfilDatosError = document.getElementById("perfil-datos-error");
const perfilPasswordForm = document.getElementById("perfil-password-form");
const perfilPasswordError = document.getElementById("perfil-password-error");
const perfilRolBadge = document.getElementById("perfil-rol-badge");

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
  }, 3400);
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
  const d = isoString ? new Date(isoString) : new Date(Date.now() + 3600000);
  if (Number.isNaN(d.getTime())) return "";
  const offsetMs = d.getTimezoneOffset() * 60000;
  return new Date(d.getTime() - offsetMs).toISOString().slice(0, 16);
}

function currentDatetimeLocalMin() {
  const now = new Date();
  const offsetMs = now.getTimezoneOffset() * 60000;
  return new Date(now.getTime() - offsetMs).toISOString().slice(0, 16);
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

function updateTopbarUser(user) {
  if (!user) return;
  const admin = String(user.rol || "").toLowerCase() === "admin";
  currentUserLabel.textContent = `${user.nombreCompleto} (@${user.username})`;
  currentRoleBadge.textContent = admin ? "Admin" : "Usuario";
  currentRoleBadge.className = `role-badge ${admin ? "role-admin" : "role-usuario"}`;
  if (perfilRolBadge) {
    perfilRolBadge.textContent = admin ? "Administrador" : "Usuario";
    perfilRolBadge.className = `role-badge ${admin ? "role-admin" : "role-usuario"}`;
  }
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

function setActiveTab(targetTab) {
  tabButtons.forEach((b) => b.classList.toggle("active", b.dataset.tab === targetTab));
  tabPanels.forEach((panel) => {
    panel.classList.toggle("hidden", panel.id !== `tab-${targetTab}`);
  });
}

function applyRoleLayout() {
  const admin = isAdminUser();
  tabBtnClientes.classList.toggle("hidden", !admin);
  mascotasAdminFilter.classList.toggle("hidden", !admin);
  filterConsultaCliente.classList.toggle("hidden", !admin);

  tabBtnMascotas.textContent = admin ? "🐕 Caninos / Mascotas" : "🐕 Mis Mascotas";
  tabBtnConsultas.textContent = admin ? "🩺 Consultas y Servicios" : "🩺 Mis Consultas";
  mascotasHeading.textContent = admin ? "Gestión de Caninos" : "Mis Mascotas";
  mascotasSubtitle.textContent = admin
    ? "Visualiza fotos, próxima cita e historial por canino o filtra por propietario."
    : "Haz clic en una mascota para ver su ficha detallada, próxima cita e historial.";
  consultasHeading.textContent = admin ? "Consultas y Servicios" : "Mis Consultas y Citas";

  if (!admin) {
    setActiveTab("mascotas");
  } else {
    setActiveTab("clientes");
  }
}

async function showDashboard({ replaceUrl = false } = {}) {
  sessionExpiredHandled = false;
  const user = getCurrentUser();
  updateTopbarUser(user);

  authSection.classList.add("hidden");
  dashboardSection.classList.remove("hidden");
  perfilSection.classList.add("hidden");
  tabsNav.classList.remove("hidden");
  dashboardMain.classList.remove("hidden");

  applyRoleLayout();
  navigatePath("/dashboard", { replace: replaceUrl });

  state.selectedMascotaId = null;

  if (isAdminUser()) {
    await Promise.all([loadClientes(), loadMascotas(), loadConsultas()]);
  } else {
    state.allClientes = user ? [user] : [];
    state.clientes = state.allClientes;
    await Promise.all([loadMascotas(), loadConsultas()]);
  }
}

async function showPerfil({ replaceUrl = false } = {}) {
  sessionExpiredHandled = false;
  authSection.classList.add("hidden");
  dashboardSection.classList.remove("hidden");
  tabsNav.classList.add("hidden");
  dashboardMain.classList.add("hidden");
  perfilSection.classList.remove("hidden");

  hideError(perfilDatosError);
  hideError(perfilPasswordError);
  perfilPasswordForm.reset();

  navigatePath("/perfil", { replace: replaceUrl });

  try {
    const profile = await apiRequest("/perfil");
    updateCurrentUser(profile);
    updateTopbarUser(profile);
    document.getElementById("perfil-username").value = profile.username || "";
    document.getElementById("perfil-documento").value = profile.documento || "";
    document.getElementById("perfil-nombre").value = profile.nombreCompleto || "";
    document.getElementById("perfil-telefono").value = profile.telefono || "";
    document.getElementById("perfil-email").value = profile.email || "";
    document.getElementById("perfil-direccion").value = profile.direccion || "";
  } catch (err) {
    renderApiError(perfilDatosError, err);
  }
}

function showAuth({ register = false, replaceUrl = true } = {}) {
  clearSession();
  state.allClientes = [];
  state.clientes = [];
  state.allMascotas = [];
  state.mascotas = [];
  state.consultas = [];
  state.selectedMascotaId = null;

  dashboardSection.classList.add("hidden");
  authSection.classList.remove("hidden");
  loginForm.reset();
  registerForm.reset();
  applyAuthMode(register, { syncUrl: false });
  navigatePath(register ? "/registro" : "/login", { replace: replaceUrl });
}

window.addEventListener("popstate", () => {
  if (!useCleanRoutes) return;
  const path = window.location.pathname.toLowerCase();
  if (getToken()) {
    if (path === "/perfil") {
      showPerfil({ replaceUrl: true });
    } else {
      showDashboard({ replaceUrl: true });
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

btnOpenPerfil.addEventListener("click", () => showPerfil());
btnBackDashboard.addEventListener("click", () => showDashboard());
topbarHome.addEventListener("click", () => {
  if (getToken()) showDashboard();
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

// ==================== PERFIL (/api/perfil) ====================
perfilDatosForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideError(perfilDatosError);

  const payload = {
    username: document.getElementById("perfil-username").value.trim(),
    documento: document.getElementById("perfil-documento").value.trim(),
    nombreCompleto: document.getElementById("perfil-nombre").value.trim(),
    telefono: document.getElementById("perfil-telefono").value.trim(),
    email: document.getElementById("perfil-email").value.trim(),
    direccion: document.getElementById("perfil-direccion").value.trim(),
  };

  try {
    const updated = await apiRequest("/perfil", { method: "PUT", body: payload });
    updateCurrentUser(updated);
    updateTopbarUser(updated);
    showToast("Datos personales actualizados correctamente.", "success");
  } catch (err) {
    renderApiError(perfilDatosError, err);
  }
});

perfilPasswordForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  hideError(perfilPasswordError);

  const passwordActual = document.getElementById("pwd-actual").value;
  const passwordNueva = document.getElementById("pwd-nueva").value;
  const confirmarPassword = document.getElementById("pwd-confirmar").value;

  const localErrors = {};
  if (!isValidNumericPassword(passwordActual)) {
    localErrors.passwordActual = ["Debe ser numérica de 5 a 20 dígitos."];
  }
  if (!isValidNumericPassword(passwordNueva)) {
    localErrors.passwordNueva = ["Debe ser numérica de 5 a 20 dígitos."];
  }
  if (passwordNueva !== confirmarPassword) {
    localErrors.confirmarPassword = ["La confirmación no coincide con la nueva contraseña."];
  }

  if (Object.keys(localErrors).length > 0) {
    renderApiError(perfilPasswordError, { message: "Error de validación local.", errors: localErrors });
    return;
  }

  try {
    await apiRequest("/perfil/password", {
      method: "PUT",
      body: { passwordActual, passwordNueva, confirmarPassword },
    });
    perfilPasswordForm.reset();
    showToast("Contraseña actualizada correctamente.", "success");
  } catch (err) {
    renderApiError(perfilPasswordError, err);
  }
});

// ==================== PESTAÑAS ====================
tabButtons.forEach((btn) => {
  btn.addEventListener("click", () => {
    const targetTab = btn.dataset.tab;
    if (targetTab === "clientes" && !isAdminUser()) return;
    setActiveTab(targetTab);
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
// MÓDULO 1: CLIENTES / USUARIOS (/api/usuarios - SOLO ADMIN)
// =========================================================
async function loadClientes(search = "") {
  if (!isAdminUser()) return;
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
  if (!isAdminUser()) return;
  try {
    const full = await apiRequest("/usuarios");
    state.allClientes = Array.isArray(full) ? full : [];
    populateClienteSelects();
  } catch {
    // Manejado por apiRequest
  }
}

function renderClientesTable() {
  if (state.clientes.length === 0) {
    tbodyClientes.innerHTML = `<tr><td colspan="8" class="empty-row">No hay usuarios registrados.</td></tr>`;
    return;
  }

  tbodyClientes.innerHTML = state.clientes
    .map((c) => {
      const roleClass = c.rol === "admin" ? "role-admin" : "role-usuario";
      const roleLabel = c.rol === "admin" ? "Admin" : "Usuario";
      return `
      <tr>
        <td>${c.id}</td>
        <td><strong>${escapeHtml(c.username)}</strong></td>
        <td><span class="role-badge ${roleClass}">${roleLabel}</span></td>
        <td>${escapeHtml(c.documento)}</td>
        <td>${escapeHtml(c.nombreCompleto)}</td>
        <td>${escapeHtml(c.telefono)}</td>
        <td>${escapeHtml(c.email)}</td>
        <td class="actions-cell">
          <button type="button" class="btn btn-outline btn-sm" data-edit-cliente="${c.id}">Editar</button>
          <button type="button" class="btn btn-danger btn-sm" data-del-cliente="${c.id}">Eliminar</button>
        </td>
      </tr>
    `;
    })
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
    isEdit ? `Editar Usuario #${cliente.id}` : "Registrar Nuevo Usuario",
    `
    <form id="cliente-modal-form" class="form-stack">
      <div class="form-grid-2">
        <div class="form-group">
          <label for="cli-username">Usuario *</label>
          <input type="text" id="cli-username" value="${escapeHtml(cliente?.username || "")}" required />
        </div>
        <div class="form-group">
          <label for="cli-rol">Rol *</label>
          <select id="cli-rol" required>
            <option value="usuario" ${(!cliente || cliente.rol === "usuario") ? "selected" : ""}>Usuario</option>
            <option value="admin" ${cliente?.rol === "admin" ? "selected" : ""}>Administrador</option>
          </select>
        </div>
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
        ${isEdit ? "Guardar Cambios" : "Registrar Usuario"}
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
      rol: document.getElementById("cli-rol").value,
      documento: document.getElementById("cli-documento").value.trim(),
      nombreCompleto: document.getElementById("cli-nombre").value.trim(),
      telefono: document.getElementById("cli-telefono").value.trim(),
      email: document.getElementById("cli-email").value.trim(),
      direccion: document.getElementById("cli-direccion").value.trim(),
    };

    if (!isEdit || rawPwd.length > 0) {
      payload.password = rawPwd;
    }

    try {
      if (isEdit) {
        const updated = await apiRequest(`/usuarios/${cliente.id}`, { method: "PUT", body: payload });
        const currentUser = getCurrentUser();
        if (currentUser && String(currentUser.id) === String(updated.id)) {
          updateCurrentUser(updated);
          updateTopbarUser(updated);
        }
        showToast("Usuario actualizado correctamente.", "success");
      } else {
        await apiRequest("/usuarios", { method: "POST", body: payload });
        showToast("Usuario registrado correctamente.", "success");
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
    if (!confirm(`¿Deseas eliminar al usuario #${delId}? (Si tiene mascotas o consultas asociadas, el servidor lo bloqueará).`)) {
      return;
    }
    try {
      await apiRequest(`/usuarios/${delId}`, { method: "DELETE" });
      showToast("Usuario eliminado.", "success");
      await refreshClientesCatalog();
      await loadClientes(searchClientesInput.value);
    } catch (err) {
      showToast(err.message, "error");
    }
  }
});

// =========================================================
// MÓDULO 2: CANINOS / MASCOTAS (GRID VIEW + TARJETA CENTRADA)
// =========================================================
async function loadMascotas(userId = "") {
  try {
    const effectiveFilter = isAdminUser() ? userId : "";
    const query = effectiveFilter ? `?userId=${encodeURIComponent(effectiveFilter)}` : "";
    const data = await apiRequest(`/mascotas${query}`);
    state.mascotas = Array.isArray(data) ? data : [];

    if (!effectiveFilter) {
      state.allMascotas = state.mascotas;
    } else if (state.allMascotas.length === 0) {
      const full = await apiRequest("/mascotas");
      state.allMascotas = Array.isArray(full) ? full : [];
    }

    // Si la mascota seleccionada ya no existe en la lista actual, limpiamos selección
    if (
      state.selectedMascotaId &&
      !state.mascotas.some((m) => String(m.id) === String(state.selectedMascotaId))
    ) {
      state.selectedMascotaId = null;
    }

    renderMascotasView();
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
    // Manejado por apiRequest
  }
}

function renderPhotoMarkup(mascota) {
  if (mascota.fotoUrl) {
    const src = escapeHtml(resolveAssetUrl(mascota.fotoUrl));
    return `<img src="${src}" alt="Foto de ${escapeHtml(mascota.nombre)}" loading="lazy" />`;
  }
  return `
    <div class="pet-photo-placeholder">
      <span>🐕</span>
      <small>Sin foto registrada</small>
    </div>
  `;
}

function renderNextCitaBlock(proximaCita) {
  if (!proximaCita) {
    return `
      <div class="next-cita-box empty">
        <div class="next-cita-label">📅 Próxima Cita</div>
        <div>Sin citas próximas programadas</div>
      </div>
    `;
  }

  const srvKey = String(proximaCita.tipoServicio || "").toLowerCase();
  return `
    <div class="next-cita-box">
      <div class="next-cita-label">📅 Próxima Cita</div>
      <div>
        <span class="badge badge-${srvKey}">${escapeHtml(proximaCita.tipoServicio)}</span>
        <strong>${escapeHtml(formatDate(proximaCita.fechaConsulta))}</strong>
      </div>
      <div>${escapeHtml(proximaCita.descripcion)}</div>
    </div>
  `;
}

function renderMascotasView() {
  // Caso 0: sin mascotas
  if (state.mascotas.length === 0) {
    mascotasViewContainer.innerHTML = `
      <div class="pets-empty-card">
        <div class="pets-empty-icon">🐾</div>
        <h3>Aún no hay caninos registrados</h3>
        <p class="hint">Registra tu primer canino con su foto para llevar el control de citas e historial.</p>
        <button type="button" class="btn btn-primary" data-empty-new-mascota="true">+ Registrar Canino</button>
      </div>
    `;
    return;
  }

  // Caso 1: exactamente 1 mascota O una mascota seleccionada al hacer clic en el grid -> Vista Centrada
  if (state.mascotas.length === 1 || state.selectedMascotaId) {
    const pet =
      state.mascotas.length === 1
        ? state.mascotas[0]
        : state.mascotas.find((m) => String(m.id) === String(state.selectedMascotaId)) || state.mascotas[0];

    renderCenteredPetCard(pet, { showBackButton: state.mascotas.length > 1 });
    return;
  }

  // Caso 2: más de 1 mascota -> Grid View con foto, próxima cita y campo historial
  const admin = isAdminUser();
  mascotasViewContainer.innerHTML = `
    <div class="pets-grid">
      ${state.mascotas
        .map((m) => {
          const historialText =
            m.totalHistorial === 1 ? "1 cita previa" : `${m.totalHistorial || 0} citas previas`;
          return `
          <article class="pet-grid-card" data-select-mascota="${m.id}">
            <div class="pet-photo-frame">
              ${renderPhotoMarkup(m)}
              ${admin ? `<span class="pet-owner-pill">👤 ${escapeHtml(m.propietarioNombre)}</span>` : ""}
            </div>
            <div class="pet-grid-body">
              <div class="pet-card-title-row">
                <h3>${escapeHtml(m.nombre)}</h3>
                <span class="badge badge-salud">${escapeHtml(m.sexo)}</span>
              </div>
              <p class="pet-meta-line">
                ${escapeHtml(m.raza)} · ${m.edad} ${m.edad === 1 ? "año" : "años"} · ${m.pesoKg} kg
              </p>

              ${renderNextCitaBlock(m.proximaCita)}

              <div class="pet-history-field">
                <span><strong>📋 Historial:</strong> ${historialText}</span>
                <button
                  type="button"
                  class="btn btn-outline btn-sm"
                  data-history-mascota="${m.id}"
                >Ver historial</button>
              </div>
            </div>
          </article>
        `;
        })
        .join("")}
    </div>
  `;
}

function renderCenteredPetCard(pet, { showBackButton }) {
  const admin = isAdminUser();
  const historialText =
    pet.totalHistorial === 1 ? "1 cita previa" : `${pet.totalHistorial || 0} citas previas`;

  mascotasViewContainer.innerHTML = `
    <div class="pet-spotlight-wrapper">
      ${
        showBackButton
          ? `<div class="pet-spotlight-toolbar">
               <button type="button" class="btn btn-outline btn-sm" data-back-grid="true">← Volver a la cuadrícula (${state.mascotas.length} mascotas)</button>
             </div>`
          : ""
      }

      <article class="pet-spotlight-card">
        <div class="pet-spotlight-hero">
          <div class="pet-spotlight-photo">
            ${renderPhotoMarkup(pet)}
            ${admin ? `<span class="pet-owner-pill">👤 ${escapeHtml(pet.propietarioNombre)}</span>` : ""}
          </div>

          <div class="pet-spotlight-info">
            <div class="pet-card-title-row">
              <h3>${escapeHtml(pet.nombre)}</h3>
              <span class="badge badge-salud">${escapeHtml(pet.sexo)}</span>
            </div>

            <div class="pet-specs-grid">
              <div class="pet-spec-item"><small>Raza</small><strong>${escapeHtml(pet.raza)}</strong></div>
              <div class="pet-spec-item"><small>Edad</small><strong>${pet.edad} ${pet.edad === 1 ? "año" : "años"}</strong></div>
              <div class="pet-spec-item"><small>Peso</small><strong>${pet.pesoKg} kg</strong></div>
              <div class="pet-spec-item"><small>Color</small><strong>${escapeHtml(pet.color)}</strong></div>
              <div class="pet-spec-item"><small>Esterilizado</small><strong>${pet.esterilizado ? "Sí" : "No"}</strong></div>
              <div class="pet-spec-item"><small>Propietario</small><strong>${escapeHtml(pet.propietarioNombre || `ID ${pet.userId}`)}</strong></div>
            </div>

            ${
              pet.observaciones
                ? `<p class="hint"><strong>Observaciones:</strong> ${escapeHtml(pet.observaciones)}</p>`
                : ""
            }
          </div>
        </div>

        <div class="pet-spotlight-footer">
          ${renderNextCitaBlock(pet.proximaCita)}

          <div class="pet-history-field">
            <span><strong>📋 Historial de citas previas:</strong> ${historialText}</span>
            <button type="button" class="btn btn-secondary btn-sm" data-history-mascota="${pet.id}">
              Consultar Historial
            </button>
          </div>

          <div class="pet-spotlight-actions">
            <button type="button" class="btn btn-primary btn-sm" data-schedule-mascota="${pet.id}">
              + Programar Cita
            </button>
            <button type="button" class="btn btn-outline btn-sm" data-edit-mascota="${pet.id}">
              Editar Mascota
            </button>
            <button type="button" class="btn btn-danger btn-sm" data-del-mascota="${pet.id}">
              Eliminar
            </button>
          </div>
        </div>
      </article>
    </div>
  `;
}

async function openMascotaHistorialModal(mascotaId) {
  openModal("Historial de Citas", `<p class="hint">Cargando historial indexado...</p>`);

  try {
    const data = await apiRequest(`/mascotas/${mascotaId}/historial`);
    const previas = data.citasPrevias || [];
    const todas = data.todasLasConsultas || [];

    const renderList = (items, emptyText) => {
      if (items.length === 0) {
        return `<p class="hint">${emptyText}</p>`;
      }
      return `
        <div class="history-list">
          ${items
            .map((c) => {
              const srvKey = String(c.tipoServicio || "").toLowerCase();
              const estKey = String(c.estado || "").toLowerCase();
              return `
              <div class="history-item">
                <div class="history-item-top">
                  <div>
                    <span class="badge badge-${srvKey}">${escapeHtml(c.tipoServicio)}</span>
                    <span class="badge badge-${estKey}">${escapeHtml(c.estado)}</span>
                  </div>
                  <strong>${escapeHtml(formatDate(c.fechaConsulta))}</strong>
                </div>
                <div>${escapeHtml(c.descripcion)}</div>
                <div class="hint">
                  Costo: <strong>${escapeHtml(formatCurrency(c.costo))}</strong>
                  ${c.observaciones ? ` · Obs: ${escapeHtml(c.observaciones)}` : ""}
                </div>
              </div>
            `;
            })
            .join("")}
        </div>
      `;
    };

    openModal(
      `📋 Historial de ${data.mascotaNombre} (${data.mascotaRaza})`,
      `
      <div class="form-stack">
        ${renderNextCitaBlock(data.proximaCita)}

        <div>
          <h4>Citas Previas / Finalizadas (${previas.length})</h4>
          ${renderList(previas, "Esta mascota aún no tiene citas previas o completadas.")}
        </div>

        <div>
          <h4>Todas las Consultas Registradas (${todas.length})</h4>
          ${renderList(todas, "Sin consultas registradas.")}
        </div>
      </div>
    `
    );
  } catch (err) {
    openModal("Error al cargar historial", `<div class="form-error">${escapeHtml(err.message)}</div>`);
  }
}

document.getElementById("btn-filter-mascotas").addEventListener("click", () => {
  state.selectedMascotaId = null;
  loadMascotas(filterMascotaCliente.value);
});

document.getElementById("btn-reset-mascotas").addEventListener("click", () => {
  filterMascotaCliente.value = "";
  state.selectedMascotaId = null;
  loadMascotas("");
});

document.getElementById("btn-new-mascota").addEventListener("click", () => {
  if (isAdminUser() && state.allClientes.length === 0) {
    showToast("Primero debes registrar al menos un cliente.", "error");
    return;
  }
  openMascotaModal(null);
});

mascotasViewContainer.addEventListener("click", async (e) => {
  const emptyNewBtn = e.target.closest("[data-empty-new-mascota]");
  if (emptyNewBtn) {
    openMascotaModal(null);
    return;
  }

  const backBtn = e.target.closest("[data-back-grid]");
  if (backBtn) {
    state.selectedMascotaId = null;
    renderMascotasView();
    return;
  }

  const historyBtn = e.target.closest("[data-history-mascota]");
  if (historyBtn) {
    e.stopPropagation();
    await openMascotaHistorialModal(historyBtn.dataset.historyMascota);
    return;
  }

  const scheduleBtn = e.target.closest("[data-schedule-mascota]");
  if (scheduleBtn) {
    e.stopPropagation();
    const pet = state.allMascotas.find((m) => String(m.id) === String(scheduleBtn.dataset.scheduleMascota));
    if (pet) {
      openConsultaModal(null, { preselectedUserId: pet.userId, preselectedMascotaId: pet.id });
    }
    return;
  }

  const editBtn = e.target.closest("[data-edit-mascota]");
  if (editBtn) {
    e.stopPropagation();
    const mascota = state.mascotas.find((m) => String(m.id) === String(editBtn.dataset.editMascota));
    if (mascota) openMascotaModal(mascota);
    return;
  }

  const delBtn = e.target.closest("[data-del-mascota]");
  if (delBtn) {
    e.stopPropagation();
    const delId = delBtn.dataset.delMascota;
    if (!confirm(`¿Deseas eliminar al canino #${delId}?`)) return;
    try {
      await apiRequest(`/mascotas/${delId}`, { method: "DELETE" });
      showToast("Canino eliminado.", "success");
      state.selectedMascotaId = null;
      await refreshMascotasCatalog();
      await loadMascotas(filterMascotaCliente.value);
    } catch (err) {
      showToast(err.message, "error");
    }
    return;
  }

  const card = e.target.closest("[data-select-mascota]");
  if (card) {
    state.selectedMascotaId = card.dataset.selectMascota;
    renderMascotasView();
  }
});

function openMascotaModal(mascota) {
  const isEdit = Boolean(mascota);
  const admin = isAdminUser();
  const currentUser = getCurrentUser();

  const clienteOptions = state.allClientes
    .map(
      (c) =>
        `<option value="${c.id}" ${
          mascota && String(mascota.userId) === String(c.id) ? "selected" : ""
        }>${escapeHtml(c.nombreCompleto)} (${escapeHtml(c.documento)})</option>`
    )
    .join("");

  const previewUrl = mascota?.fotoUrl ? escapeHtml(resolveAssetUrl(mascota.fotoUrl)) : "";

  openModal(
    isEdit ? `Editar Canino #${mascota.id}` : "Registrar Nuevo Canino",
    `
    <form id="mascota-modal-form" class="form-stack">
      ${
        admin
          ? `<div class="form-group">
               <label for="mas-userId">Cliente Propietario *</label>
               <select id="mas-userId" required>
                 <option value="">Selecciona un cliente...</option>
                 ${clienteOptions}
               </select>
             </div>`
          : `<input type="hidden" id="mas-userId" value="${currentUser?.id || ""}" />`
      }

      <div class="form-group">
        <label>Foto del Canino (archivo o URL)</label>
        <div class="photo-upload-box">
          <img
            id="mas-foto-preview"
            class="photo-preview-thumb ${previewUrl ? "" : "hidden"}"
            src="${previewUrl}"
            alt="Vista previa"
          />
          <div class="form-stack" style="flex:1; gap:8px;">
            <input type="file" id="mas-foto-file" accept="image/jpeg,image/png,image/webp" />
            <input
              type="text"
              id="mas-fotoUrl"
              placeholder="O pega una URL (https://...)"
              value="${escapeHtml(mascota?.fotoUrl || "")}"
            />
          </div>
        </div>
        <small class="hint">Formatos permitidos: JPG, PNG o WEBP (máx. 5 MB).</small>
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
  const fileInput = document.getElementById("mas-foto-file");
  const fotoUrlInput = document.getElementById("mas-fotoUrl");
  const previewImg = document.getElementById("mas-foto-preview");

  function updatePreview(url) {
    if (url) {
      previewImg.src = resolveAssetUrl(url);
      previewImg.classList.remove("hidden");
    } else {
      previewImg.classList.add("hidden");
    }
  }

  fotoUrlInput.addEventListener("input", () => updatePreview(fotoUrlInput.value.trim()));

  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0];
    if (!file) return;
    hideError(errBox);
    try {
      const result = await uploadMascotaPhoto(file);
      fotoUrlInput.value = result.fotoUrl;
      updatePreview(result.fotoUrl);
      showToast("Foto cargada correctamente.", "success");
    } catch (err) {
      renderApiError(errBox, err);
    }
  });

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    hideError(errBox);

    const obsVal = document.getElementById("mas-observaciones").value.trim();
    const fotoVal = fotoUrlInput.value.trim();

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
      fotoUrl: fotoVal.length > 0 ? fotoVal : null,
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

// =========================================================
// MÓDULO 3: CONSULTAS Y SERVICIOS (/api/consultas)
// =========================================================
async function loadConsultas() {
  const userId = isAdminUser() ? filterConsultaCliente.value : "";
  const mascotaId = filterConsultaMascota.value;
  const tipoServicio = filterConsultaServicio.value;
  const estado = filterConsultaEstado.value;

  let endpoint = "/consultas";
  const params = new URLSearchParams();

  if (userId) params.set("userId", userId);
  if (mascotaId) params.set("mascotaId", mascotaId);
  if (tipoServicio) params.set("tipoServicio", tipoServicio);
  if (estado) params.set("estado", estado);

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
  if (state.allMascotas.length === 0) {
    showToast("Necesitas registrar al menos un canino para programar una consulta.", "error");
    return;
  }
  openConsultaModal(null);
});

function openConsultaModal(consulta, { preselectedUserId = null, preselectedMascotaId = null } = {}) {
  const isEdit = Boolean(consulta);
  const admin = isAdminUser();
  const currentUser = getCurrentUser();
  const initialUserId = consulta?.userId ?? preselectedUserId ?? currentUser?.id;
  const initialMascotaId = consulta?.mascotaId ?? preselectedMascotaId ?? null;

  const clienteOptions = state.allClientes
    .map(
      (c) =>
        `<option value="${c.id}" ${
          String(initialUserId) === String(c.id) ? "selected" : ""
        }>${escapeHtml(c.nombreCompleto)}</option>`
    )
    .join("");

  const minDateAttr = !isEdit || consulta?.estado === "Programada" ? currentDatetimeLocalMin() : "";

  openModal(
    isEdit ? `Editar Consulta #${consulta.id}` : "Programar Nueva Consulta",
    `
    <form id="consulta-modal-form" class="form-stack">
      <div class="${admin ? "form-grid-2" : ""}">
        ${
          admin
            ? `<div class="form-group">
                 <label for="con-userId">Cliente *</label>
                 <select id="con-userId" required>
                   <option value="">Selecciona un cliente...</option>
                   ${clienteOptions}
                 </select>
               </div>`
            : `<input type="hidden" id="con-userId" value="${currentUser?.id || ""}" />`
        }

        <div class="form-group">
          <label for="con-mascotaId">Canino *</label>
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
            <option value="Programada" ${(!consulta || consulta.estado === "Programada") ? "selected" : ""}>Programada</option>
            <option value="EnProceso" ${consulta?.estado === "EnProceso" ? "selected" : ""}>En Proceso</option>
            <option value="Completada" ${consulta?.estado === "Completada" ? "selected" : ""}>Completada</option>
            <option value="Cancelada" ${consulta?.estado === "Cancelada" ? "selected" : ""}>Cancelada</option>
          </select>
        </div>
      </div>

      <div class="form-grid-2">
        <div class="form-group">
          <label for="con-fechaConsulta">Fecha y Hora de Cita *</label>
          <input
            type="datetime-local"
            id="con-fechaConsulta"
            value="${toDatetimeLocalValue(consulta?.fechaConsulta)}"
            ${minDateAttr ? `min="${minDateAttr}"` : ""}
            required
          />
          <small class="hint">No se pueden programar citas antes de la fecha y hora actual.</small>
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
        ${isEdit ? "Guardar Cambios" : "Programar Consulta"}
      </button>
    </form>
  `
  );

  const selectCliente = document.getElementById("con-userId");
  const selectMascota = document.getElementById("con-mascotaId");
  const estadoSelect = document.getElementById("con-estado");
  const fechaInput = document.getElementById("con-fechaConsulta");

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

  if (admin) {
    selectCliente.addEventListener("change", () => syncMascotasForCliente(null));
  }
  syncMascotasForCliente(initialMascotaId);

  estadoSelect.addEventListener("change", () => {
    if (!isEdit || estadoSelect.value === "Programada") {
      fechaInput.min = currentDatetimeLocalMin();
    } else {
      fechaInput.removeAttribute("min");
    }
  });

  const form = document.getElementById("consulta-modal-form");
  const errBox = document.getElementById("consulta-modal-error");

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    hideError(errBox);

    const localDateStr = fechaInput.value;
    const parsedDate = new Date(localDateStr);
    if (!localDateStr || Number.isNaN(parsedDate.getTime())) {
      renderApiError(errBox, {
        message: "Error de validación local.",
        errors: { fechaConsulta: ["Debes seleccionar una fecha y hora válidas."] },
      });
      return;
    }

    const estadoVal = estadoSelect.value;
    if ((!isEdit || estadoVal === "Programada") && parsedDate.getTime() < Date.now()) {
      renderApiError(errBox, {
        message: "Error de validación local.",
        errors: { fechaConsulta: ["No se puede programar una cita antes de la fecha y hora actual."] },
      });
      return;
    }

    const obsVal = document.getElementById("con-observaciones").value.trim();

    const payload = {
      userId: parseInt(selectCliente.value, 10),
      mascotaId: parseInt(selectMascota.value, 10),
      tipoServicio: document.getElementById("con-tipoServicio").value,
      descripcion: document.getElementById("con-descripcion").value.trim(),
      fechaConsulta: parsedDate.toISOString(),
      costo: parseFloat(document.getElementById("con-costo").value),
      estado: estadoVal,
      observaciones: obsVal.length > 0 ? obsVal : null,
    };

    try {
      if (isEdit) {
        await apiRequest(`/consultas/${consulta.id}`, { method: "PUT", body: payload });
        showToast("Consulta actualizada correctamente.", "success");
      } else {
        await apiRequest("/consultas", { method: "POST", body: payload });
        showToast("Consulta programada correctamente.", "success");
      }
      closeModal();
      await Promise.all([
        loadConsultas(),
        loadMascotas(filterMascotaCliente.value),
      ]);
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
      await Promise.all([
        loadConsultas(),
        loadMascotas(filterMascotaCliente.value),
      ]);
    } catch (err) {
      showToast(err.message, "error");
    }
  }
});

// ==================== ARRANQUE INICIAL ====================
const initialPath = window.location.pathname.toLowerCase();
if (getToken()) {
  if (initialPath === "/perfil") {
    showPerfil({ replaceUrl: true });
  } else {
    showDashboard({ replaceUrl: true });
  }
} else {
  showAuth({ register: initialPath === "/registro", replaceUrl: true });
}
