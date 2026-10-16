// Analytics dashboard charts (WL-5).
// Reads the numbers from /Admin/Analytics?handler=Data for the range on the page and draws them with Chart.js.
// The page's tables stay as the accessible fallback, so if this script fails nothing is lost.
(function () {
    "use strict";

    const container = document.getElementById("dashboard-charts");
    if (!container || typeof Chart === "undefined") {
        return;
    }

    // Brand colours from site.css, one per status, so each status always has the same colour.
    const statusColours = {
        "Completed": "#12867a",
        "Seated": "#d81159",
        "Confirmed": "#ffbc42",
        "Pending": "#ff8c42",
        "Cancelled": "#9a9cb0",
        "No-show": "#5c5f77"
    };

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
                backgroundColor: statusColours[series.name] || "#5c5f77",
                stack: "bookings",
                yAxisID: "y"
            };
        });

        datasets.push({
            type: "line",
            label: "Guests served",
            data: data.guestsServed,
            borderColor: "#2b2d42",
            backgroundColor: "#2b2d42",
            tension: 0.25,
            pointRadius: 2,
            yAxisID: "guests"
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
                    backgroundColor: "#d81159"
                }]
            },
            options: {
                maintainAspectRatio: false,
                scales: { y: { beginAtZero: true, title: { display: true, text: "Table-hours" } } },
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
        })
        .catch(showError);
})();
