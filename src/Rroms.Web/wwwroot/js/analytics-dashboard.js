// Analytics dashboard charts (WL-5).
// Reads the numbers from /Admin/Analytics?handler=Data for the range on the page and draws them with Chart.js.
// The page's tables stay as the accessible fallback, so if this script fails nothing is lost.
(function () {
    "use strict";

    const container = document.getElementById("dashboard-charts");
    if (!container || typeof Chart === "undefined") {
        return;
    }

    // Gilded colours (site.css), one per status, so each status always has the same colour.
    const isDark = document.documentElement.getAttribute("data-bs-theme") === "dark";
    const inkColour = isDark ? "#f4eee6" : "#221c15";
    const statusColours = {
        "Completed": "#2f7d5b",
        "Not finished yet": "#c9a45c",
        "Seated": "#8a6424",
        "Confirmed": "#c9a45c",
        "Pending": "#e2c27e",
        "Cancelled": "#b9afa2",
        "Absent": "#c05b4e"
    };

    // Axis text, grid lines and font follow the page's light or dark look.
    Chart.defaults.color = isDark ? "#c9bba6" : "#675b4d";
    Chart.defaults.borderColor = isDark ? "rgba(244, 238, 230, 0.1)" : "rgba(90, 70, 40, 0.12)";
    Chart.defaults.font.family = "Jost, 'Segoe UI', Roboto, Arial, sans-serif";

    function showError() {
        const error = document.getElementById("charts-error");
        if (error) {
            error.classList.remove("d-none");
        }
    }

    function drawBookings(data) {
        const datasets = data.bookingsByStatus.map(function (series) {
            return {
                type: "bar",
                label: series.name,
                data: series.values,
                backgroundColor: statusColours[series.name] || "#b9afa2",
                stack: "bookings",
                yAxisID: "y",
                order: 2
            };
        });

        datasets.push({
            type: "line",
            label: "Guests served",
            data: data.guestsServed,
            borderColor: inkColour,
            backgroundColor: inkColour,
            tension: 0.25,
            pointRadius: 2,
            yAxisID: "guests",
            // Chart.js draws a lower "order" last, so the guests line sits in front of the bars.
            order: 1
        });

        new Chart(document.getElementById("bookings-chart"), {
            data: { labels: data.dayLabels, datasets: datasets },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: { stacked: true, ticks: { autoSkip: true, maxRotation: 0 } },
                    y: { stacked: true, beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: "Bookings" } },
                    guests: { position: "right", beginAtZero: true, ticks: { precision: 0 }, grid: { drawOnChartArea: false }, title: { display: true, text: "Guests" } }
                },
                plugins: { legend: { position: "bottom" } }
            }
        });
    }

    function drawHours(data) {
        new Chart(document.getElementById("hours-chart"), {
            type: "bar",
            data: {
                labels: data.hourLabels,
                datasets: [{
                    label: "Table-hours used",
                    data: data.tableHoursByHour,
                    backgroundColor: "#c9a45c"
                }]
            },
            options: {
                maintainAspectRatio: false,
                scales: { y: { beginAtZero: true, title: { display: true, text: "Table-hours" } } },
                plugins: { legend: { display: false } }
            }
        });
    }

    // Paid sales per day from Darren's DA-3 report, on the same day labels as the bookings chart.
    function drawSales(data) {
        const canvas = document.getElementById("sales-chart");
        if (!canvas) {
            return;
        }

        new Chart(canvas, {
            type: "line",
            data: {
                labels: data.dayLabels,
                datasets: [{
                    label: "Paid sales (RM)",
                    data: data.dailySales,
                    borderColor: "#a7803a",
                    backgroundColor: "rgba(201, 164, 92, 0.18)",
                    fill: true,
                    tension: 0.25,
                    pointRadius: 2
                }]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: { ticks: { autoSkip: true, maxRotation: 0 } },
                    y: { beginAtZero: true, title: { display: true, text: "RM" } }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: function (item) { return "RM " + Number(item.raw).toFixed(2); } } }
                }
            }
        });
    }

    // Best-selling dishes as horizontal bars, most units at the top.
    function drawDishes(data) {
        const canvas = document.getElementById("dishes-chart");
        if (!canvas || data.popularDishNames.length === 0) {
            return;
        }

        new Chart(canvas, {
            type: "bar",
            data: {
                labels: data.popularDishNames,
                datasets: [{
                    label: "Units sold",
                    data: data.popularDishQuantities,
                    backgroundColor: "#8a6424"
                }]
            },
            options: {
                indexAxis: "y",
                maintainAspectRatio: false,
                scales: { x: { beginAtZero: true, ticks: { precision: 0 } } },
                plugins: { legend: { display: false } }
            }
        });
    }

    fetch(container.dataset.url, { headers: { "Accept": "application/json" } })
        .then(function (response) {
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            return response.json();
        })
        .then(function (data) {
            drawBookings(data);
            drawHours(data);
            if (data.salesAvailable) {
                drawSales(data);
                drawDishes(data);
            }
        })
        .catch(showError);
})();
